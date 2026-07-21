using System.Text.Json;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Domain.Recruitment;
using HRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Issues + validates candidate-portal magic-link tokens (US-REC-008 FR-1/FR-8/NFR-4/BR-6). The signing
/// secret is read from configuration (a dedicated <c>Recruitment:PortalTokenSecret</c>, falling back to
/// <c>Jwt:PrivateKey</c>) — NEVER hardcoded. Only the token HASH + expiry are persisted in
/// <c>applicant_portal_token</c> (NFR-4). Validation verifies the HMAC signature, that the embedded tenant
/// matches the current (subdomain-resolved) tenant (AC-4/BR-4), that the token has not expired (FR-8), and
/// that a matching, non-expired hash row exists (so a revoked/superseded token without a row is rejected).
/// </summary>
public sealed class ApplicantPortalTokenService : IApplicantPortalTokenService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<ApplicantPortalTokenService> _logger;
    // ISSUE-130: optional (nullable) so isolated construction/tests compile; DI injects it in production.
    private readonly IHttpContextAccessor? _httpContextAccessor;
    // DF-7: per-IP throttle behind one seam (Redis distributed counter, or DB sliding-window fallback).
    private readonly IPortalLinkIpRateLimiter _ipRateLimiter;

    /// <summary>FR-8: default magic-link validity window (days) when not configured.</summary>
    private const int DefaultExpiryDays = 30;

    /// <summary>NFR-6: basic rate-limit guard — reuse a token issued within this window instead of minting a new one.</summary>
    private const int RecentTokenWindowSeconds = 60;

    public ApplicantPortalTokenService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IConfiguration configuration,
        INotificationDispatcher dispatcher,
        ILogger<ApplicantPortalTokenService> logger,
        IPortalLinkIpRateLimiter ipRateLimiter,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _configuration = configuration;
        _dispatcher = dispatcher;
        _logger = logger;
        _ipRateLimiter = ipRateLimiter;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<Result<IssuedPortalToken>> IssueAsync(
        string applicantEmail, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IssuedPortalToken>.Failure("Tenant context is not resolved.", 400);

        var secret = ResolveSecret();
        if (string.IsNullOrEmpty(secret))
            return Result<IssuedPortalToken>.Failure("The portal token signing secret is not configured.", 500, "portal_secret_missing");

        var emailLower = (applicantEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (emailLower.Length == 0)
            return Result<IssuedPortalToken>.Failure("An applicant email is required.", 400, "email_required");

        var tenantId = _tenantContext.TenantId;
        var now = DateTime.UtcNow;

        // NFR-6 (basic guard): if a non-expired token was issued for this email very recently, do not mint a
        // fresh one. (The per-IP throttle below is now the distributed limiter — DF-7 — but this per-EMAIL
        // guard stays a simple DB check.) We can't return the prior raw token (only its hash is stored), so
        // we throttle by refusing rapid re-issue.
        var recentExists = await _dbContext.ApplicantPortalTokens
            .AsNoTracking()
            .AnyAsync(t => t.ApplicantEmail == emailLower
                           && t.ExpiresAt > now
                           && t.CreatedAt > now.AddSeconds(-RecentTokenWindowSeconds), cancellationToken);
        if (recentExists)
        {
            _logger.LogInformation(
                "Portal token issue throttled (NFR-6) for {Email} in tenant {TenantId} — a token was issued in the last {Window}s.",
                emailLower, tenantId, RecentTokenWindowSeconds);
            return Result<IssuedPortalToken>.Failure(
                "A portal link was just sent. Please wait a moment before requesting another.", 429, "rate_limited");
        }

        // ISSUE-130 (NFR-6) / DF-7: per-IP throttle. The per-email guard above misses an attacker rotating DISTINCT
        // emails from one IP (enumeration / provider-exhaustion), so also cap how many tokens a single IP may issue
        // (across all emails) within the window. Delegated to IPortalLinkIpRateLimiter — a distributed Redis counter
        // at multi-instance scale, or the DB sliding-window fallback when Redis is not configured. Skipped when the IP
        // is unknown (e.g. a background job with no HttpContext) — preserving the original behaviour.
        var requestIp = _httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(requestIp)
            && !await _ipRateLimiter.TryAcquireAsync(tenantId, requestIp, cancellationToken))
        {
            return Result<IssuedPortalToken>.Failure(
                "Too many portal link requests. Please try again later.", 429, "rate_limited");
        }

        var expiresAt = now.AddDays(DefaultExpiryDays);
        var token = PortalMagicLink.Issue(tenantId, emailLower, expiresAt, secret);
        var tokenHash = PortalMagicLink.HashToken(token);

        _dbContext.ApplicantPortalTokens.Add(new ApplicantPortalToken
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            ApplicantEmail = emailLower,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            RequestIp = requestIp,
            IsDeleted = false,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Portal magic-link token issued for {Email} in tenant {TenantId}, expires {ExpiresAt}.",
            emailLower, tenantId, expiresAt);

        return Result<IssuedPortalToken>.Success(new IssuedPortalToken
        {
            Token = token,
            ExpiresAt = expiresAt,
        });
    }

    public async Task<Result<bool>> RequestLinkAsync(string email, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<bool>.Failure("Tenant context is not resolved.", 400);

        var emailLower = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (emailLower.Length == 0)
            return Result<bool>.Failure("An email is required.", 400, "email_required");

        // BR-5 email verification: only (re)issue if an application exists for this email in the tenant. The
        // global query filter scopes this to the current tenant (AC-4). Load the applicant's first name too so the
        // email can greet them; a null row means "no application" → anti-enumeration no-op.
        var applicant = await _dbContext.Applicants
            .AsNoTracking()
            .Where(a => a.Email.ToLower() == emailLower)
            .Select(a => new { a.FirstName })
            .FirstOrDefaultAsync(cancellationToken);

        if (applicant is null)
        {
            _logger.LogInformation(
                "Portal link requested for {Email} but no application exists (BR-5) — no link issued.", emailLower);
            return Result<bool>.Success(false);
        }

        var issued = await IssueAsync(emailLower, cancellationToken);
        if (issued.IsFailure)
        {
            // Rate-limited (NFR-6): treat as "no new link" so we don't reveal existence; bubble real errors.
            if (issued.ErrorCode == "rate_limited")
                return Result<bool>.Success(false);
            return Result<bool>.Failure(issued.Error!, issued.StatusCode ?? 400, issued.ErrorCode);
        }

        // FR-7: email the candidate their magic link via the canonical templated dispatch path (DF-41). Never let a
        // dispatch failure surface as "no application" — the token is already minted; log and still return success.
        try
        {
            var portalUrl = await BuildPortalUrlAsync(issued.Value!.Token, cancellationToken);
            var companyName = await ResolveCompanyNameAsync(cancellationToken);
            var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["applicant"] = new Dictionary<string, object?> { ["firstName"] = applicant.FirstName },
                ["portal"] = new Dictionary<string, object?>
                {
                    ["url"] = portalUrl,
                    ["expiresAt"] = issued.Value.ExpiresAt.ToString("yyyy-MM-dd"),
                },
                ["tenant"] = new Dictionary<string, object?> { ["companyName"] = companyName },
            });

            var request = new NotificationRequest(
                TenantId: _tenantContext.TenantId,
                EventKey: "applicant_portal_link",
                PayloadJson: payloadJson,
                RecipientEmail: emailLower,
                NotificationType: "applicant_portal_link");
            await _dispatcher.SendEmailAsync(request, cancellationToken);

            _logger.LogInformation(
                "Portal link re-issued and emailed for {Email} (BR-5/FR-7). Expires {ExpiresAt}.",
                emailLower, issued.Value.ExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Portal link minted for {Email} but the magic-link email dispatch failed (FR-7).", emailLower);
        }

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Builds the candidate-portal magic-link URL (FR-7): <c>https://{subdomain}.{baseDomain}/portal?token=...</c>.
    /// Prefers the request-scoped <see cref="ITenantContext.Subdomain"/>; a Hangfire/job context may not have it, so
    /// it falls back to a tenant-scoped read of <c>Tenant.Subdomain</c> (IgnoreQueryFilters, by the resolved id).
    /// </summary>
    private async Task<string> BuildPortalUrlAsync(string rawToken, CancellationToken cancellationToken)
    {
        var subdomain = _tenantContext.Subdomain;
        if (string.IsNullOrWhiteSpace(subdomain))
        {
            var tenantId = _tenantContext.TenantId;
            subdomain = await _dbContext.Tenants.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.Subdomain)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        }

        var baseDomain = PortalLinkBuilder.NormalizeBaseDomain(_configuration["Platform:BaseDomain"]);
        return PortalLinkBuilder.Build(subdomain, baseDomain, rawToken);
    }

    /// <summary>Resolves the tenant's company (display) name for the email branding placeholder (tenant-scoped).</summary>
    private async Task<string?> ResolveCompanyNameAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        return await _dbContext.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Result<ValidatedPortalToken>> ValidateAsync(
        string token, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ValidatedPortalToken>.Failure("Tenant context is not resolved.", 400);

        var secret = ResolveSecret();
        if (string.IsNullOrEmpty(secret))
            return Result<ValidatedPortalToken>.Failure("The portal token signing secret is not configured.", 500, "portal_secret_missing");

        // 1. Signature + payload integrity (NFR-4). A tampered token fails here.
        if (!PortalMagicLink.TryVerify(token, secret, out var tokenTenantId, out var email, out var expiresAt))
            return Result<ValidatedPortalToken>.Failure("Invalid portal link.", 401, "invalid_token");

        // 2. Cross-tenant rejection (AC-4/BR-4): the embedded tenant MUST equal the subdomain-resolved tenant.
        if (tokenTenantId != _tenantContext.TenantId)
        {
            _logger.LogWarning(
                "Portal token tenant mismatch (AC-4): token tenant {TokenTenant} presented on tenant {CurrentTenant}.",
                tokenTenantId, _tenantContext.TenantId);
            return Result<ValidatedPortalToken>.Failure("Invalid portal link.", 401, "invalid_token");
        }

        // 3. Expiry (FR-8).
        if (expiresAt <= DateTime.UtcNow)
            return Result<ValidatedPortalToken>.Failure("This portal link has expired. Please request a new one.", 401, "token_expired");

        // 4. Stored-hash match: the token must correspond to a persisted, non-expired row (the global query
        // filter also scopes this to the current tenant — defence in depth, AC-4).
        var tokenHash = PortalMagicLink.HashToken(token);
        var rowExists = await _dbContext.ApplicantPortalTokens
            .AsNoTracking()
            .AnyAsync(t => t.TokenHash == tokenHash && t.ExpiresAt > DateTime.UtcNow, cancellationToken);
        if (!rowExists)
            return Result<ValidatedPortalToken>.Failure("Invalid portal link.", 401, "invalid_token");

        return Result<ValidatedPortalToken>.Success(new ValidatedPortalToken
        {
            ApplicantEmail = email,
            TenantId = tokenTenantId,
        });
    }

    /// <summary>
    /// Resolves the HMAC signing secret from config (never hardcoded): a dedicated
    /// <c>Recruitment:PortalTokenSecret</c> if set, else the existing <c>Jwt:PrivateKey</c>.
    /// </summary>
    private string ResolveSecret() =>
        _configuration["Recruitment:PortalTokenSecret"]
        ?? _configuration["Jwt:PrivateKey"]
        ?? string.Empty;
}
