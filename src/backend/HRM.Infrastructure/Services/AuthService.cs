using Hangfire;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using HRM.Application.Features.Auth.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Authentication service implementing login, token refresh, logout, password reset, and MFA flows.
/// Handles multi-tenant context, account lockout, and token rotation with reuse detection.
/// US-AUTH-010: Progressive lockout, audit events, lockout notification via Hangfire.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly IJwtService _jwtService;
    private readonly ITenantContext _tenantContext;
    private readonly ITotpService _totpService;
    private readonly IConfiguration _configuration;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AuthService> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;
    // ISSUE-058: needed to attribute admin session-revoke audits to the ACTING admin, not the victim.
    // Optional (nullable, default null) so isolated unit construction that omits it still compiles; the DI
    // container injects the registered scoped ICurrentUser in production.
    private readonly ICurrentUser? _currentUser;
    // US-AUTH-005 NFR-2: encrypts/decrypts the TOTP MFA secret at rest. Optional so isolated unit
    // construction that omits it still compiles; falls back to a no-op protector (plaintext passthrough),
    // preserving pre-encryption behavior. DI injects the registered singleton in production.
    private readonly IFieldProtector _mfaSecretProtector;
    // US-NTF-006 Phase 2b: dispatches the real self-service password-reset email. Optional (nullable, default
    // null) so isolated unit construction that omits it still compiles; DI injects the registered dispatcher in
    // production. A null dispatcher (or any delivery failure) never fails the reset request.
    private readonly INotificationDispatcher? _notificationDispatcher;
    // BUG-116: invalidates the user's cached my-tenants entry when a new membership is created (SSO JIT), so the
    // authorization data isn't served stale for up to the 5-min TTL. Optional (nullable, default null) so isolated
    // unit construction that omits it still compiles (mirrors the optional deps above); DI injects it in production.
    private readonly IMyTenantsCache? _myTenantsCache;

    public AuthService(
        AppDbContext dbContext,
        IJwtService jwtService,
        ITenantContext tenantContext,
        ITotpService totpService,
        IConfiguration configuration,
        IDistributedCache cache,
        ILogger<AuthService> logger,
        IBackgroundJobClient backgroundJobClient,
        ICurrentUser? currentUser = null,
        IFieldProtector? mfaSecretProtector = null,
        INotificationDispatcher? notificationDispatcher = null,
        IMyTenantsCache? myTenantsCache = null)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
        _tenantContext = tenantContext;
        _totpService = totpService;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
        _currentUser = currentUser;
        _mfaSecretProtector = mfaSecretProtector ?? PlaintextFieldProtector.Instance;
        _notificationDispatcher = notificationDispatcher;
        _myTenantsCache = myTenantsCache;
    }

    public async Task<Result<LoginResponse>> LoginAsync(
        string email,
        string password,
        string? mfaCode,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        email = email.Trim().ToLowerInvariant();

        // 1. Find user by email (global, not tenant-scoped)
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            // NFR-4: Do a dummy BCrypt.Verify to keep response time indistinguishable
            // from a real password check. This prevents timing-based user enumeration.
            BCrypt.Net.BCrypt.Verify(password, "$2a$12$000000000000000000000uDummy.HashForTimingResistance000000");
            _logger.LogWarning("Login failed: email {Email} not found", email);
            return Result<LoginResponse>.Failure("Invalid email or password.", 401);
        }

        // 2. FR-5/AC-3: Check if account is locked BEFORE verifying password
        if (user.LockedUntil.HasValue)
        {
            if (user.LockedUntil.Value > DateTime.UtcNow)
            {
                // Account is still locked -- do NOT check password (AC-3)
                // NFR-4: Do a dummy BCrypt.Verify to keep timing indistinguishable
                BCrypt.Net.BCrypt.Verify(password, user.PasswordHash ?? "$2a$12$000000000000000000000uDummy.HashForTimingResistance000000");
                _logger.LogWarning("Login failed: account locked for user {UserId} until {LockedUntil}",
                    user.Id, user.LockedUntil.Value);
                return Result<LoginResponse>.Failure(
                    "Account temporarily locked. Try again later or contact your administrator.", 401);
            }

            // AC-4: Lockout has expired -- clear lockout state and log audit event
            user.LockedUntil = null;
            user.FailedLoginCount = 0;
            user.MfaFailedAttemptCount = 0;
            await WriteAuditLogAsync(user.Id, "account_unlocked_by_timeout", ipAddress, userAgent, cancellationToken);
            _logger.LogInformation("Lockout expired for user {UserId}, cleared lockout state", user.Id);
        }

        // 3. Check if user is globally active
        if (!user.IsActive)
        {
            return Result<LoginResponse>.Failure("Invalid email or password.", 401);
        }

        // 4. Check password (social-only users have null password_hash)
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            return Result<LoginResponse>.Failure("Invalid email or password.", 401);
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            // BUG-045: run the whole failed-attempt handling as one atomic, retriable unit under a per-user
            // row lock (see RunFailedAttemptAsync), so concurrent wrong-password logins cannot lose counter
            // increments (classic lost-update) nor double-apply lockout. The lockout decision branches on the
            // count committed under the lock, not a stale in-memory read. The Hangfire email is deferred to
            // after a successful commit.
            LockoutNotification? lockoutEmail = null;
            await RunFailedAttemptAsync(user, async () =>
            {
                // Increment failed login count
                user.FailedLoginCount++;

                // Resolve tenant lockout policy
                var (maxAttempts, lockoutMinutes, progressiveLockoutEnabled) = await GetLockoutPolicyAsync(cancellationToken);

                // Audit: login_failure with attempt count
                await WriteAuditLogWithDetailAsync(user.Id, "login_failure", ipAddress, userAgent,
                    new { attemptCount = user.FailedLoginCount },
                    cancellationToken);

                if (user.FailedLoginCount >= maxAttempts)
                {
                    // Calculate effective lockout duration (progressive lockout FR-9)
                    var effectiveLockoutMinutes = CalculateProgressiveLockoutMinutes(
                        user, lockoutMinutes, progressiveLockoutEnabled);

                    user.LockedUntil = DateTime.UtcNow.AddMinutes(effectiveLockoutMinutes);
                    user.LockoutCount++;
                    user.LastLockoutAt = DateTime.UtcNow;

                    _logger.LogWarning(
                        "Account locked for user {UserId} after {Attempts} failed attempts. Duration: {Duration}m (progressive: {Progressive})",
                        user.Id, user.FailedLoginCount, effectiveLockoutMinutes, progressiveLockoutEnabled);

                    // Audit: account_locked with detail
                    await WriteAuditLogWithDetailAsync(user.Id, "account_locked", ipAddress, userAgent,
                        new { attemptCount = user.FailedLoginCount, lockedUntil = user.LockedUntil, durationMinutes = effectiveLockoutMinutes },
                        cancellationToken);

                    // FR-8/NFR-3: capture the lockout notification; enqueued only after a successful commit.
                    lockoutEmail = new LockoutNotification(user.Email, user.DisplayName, user.LockedUntil!.Value, effectiveLockoutMinutes);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            if (lockoutEmail is not null)
            {
                _backgroundJobClient.Enqueue<ILockoutNotificationService>(
                    svc => svc.SendLockoutNotificationAsync(lockoutEmail.Email, lockoutEmail.DisplayName, lockoutEmail.LockedUntil, lockoutEmail.Minutes, default));
            }

            _logger.LogWarning("Login failed: invalid password for user {UserId}, attempt {Attempt}",
                user.Id, user.FailedLoginCount);

            // BUG-044: if THIS failing attempt is the one that tripped the lockout, return the lockout
            // message immediately (the same message/shape the already-locked path returns on the next
            // request), rather than the generic message that would otherwise delay lockout feedback by one
            // request. LockedUntil is only set here when this attempt just crossed the threshold — an
            // already-expired lockout was cleared at step 2, and a still-active one returned earlier at step 2.
            // Below-threshold attempts leave LockedUntil null and still return the generic message (no
            // account enumeration).
            if (user.LockedUntil.HasValue)
            {
                return Result<LoginResponse>.Failure(
                    "Account temporarily locked. Try again later or contact your administrator.", 401);
            }

            return Result<LoginResponse>.Failure("Invalid email or password.", 401);
        }

        // 5. Resolve tenant and check membership
        if (!_tenantContext.IsResolved)
        {
            return Result<LoginResponse>.Failure("Tenant context is not resolved.", 400);
        }

        // Check tenant status
        var currentTenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, cancellationToken);

        if (currentTenant is null)
        {
            return Result<LoginResponse>.Failure("Tenant not found.", 404);
        }

        // US-ADM-004 AC-4: a Terminated tenant is unavailable to everyone (data is being / has been deleted).
        if (currentTenant.Status == TenantStatus.Terminated)
        {
            return Result<LoginResponse>.Failure(
                "This workspace is currently unavailable. Please contact support.", 403);
        }

        // Check user-tenant membership
        var userTenant = await _dbContext.UserTenants
            .IgnoreQueryFilters()
            .Include(ut => ut.UserTenantRoles)
                .ThenInclude(utr => utr.Role)
                    .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(
                ut => ut.UserId == user.Id && ut.TenantId == _tenantContext.TenantId,
                cancellationToken);

        if (userTenant is null || userTenant.Status != UserTenantStatus.Active)
        {
            return Result<LoginResponse>.Failure(
                "You do not have an active membership in this organization.", 403);
        }

        // US-ADM-004 AC-2: on a SUSPENDED tenant only Tenant Owner / Tenant Admin users may authenticate (to
        // reach the read-only suspension notice + request data export). All other users are blocked.
        if (currentTenant.Status == TenantStatus.Suspended)
        {
            var isTenantAdminOrOwner = userTenant.UserTenantRoles.Any(utr =>
                utr.Role.Name == PermissionCatalog.BuiltInRoles.TenantOwner
                || utr.Role.Name == PermissionCatalog.BuiltInRoles.TenantAdmin);
            if (!isTenantAdminOrOwner)
            {
                return Result<LoginResponse>.Failure(
                    "Your organization's account has been suspended. Please contact your administrator.", 403);
            }
        }

        // 5b. Policy-driven forced-enrollment check (AC-1, FR-7, BR-5)
        if (!user.MfaEnabled
            && currentTenant.MfaPolicy == "required"
            && userTenant.UserTenantRoles.Any(utr =>
                currentTenant.MfaRequiredRoles.Contains(utr.Role.Name)))
        {
            return Result<LoginResponse>.Success(new LoginResponse
            {
                MfaChallenge = true,
                MfaMethod = "totp",
                MfaEnrollmentRequired = true,
                User = new UserDto(user.Id, user.Email, user.DisplayName),
                Tenant = new TenantDto(currentTenant.Id, currentTenant.Subdomain, currentTenant.Name),
            });
        }

        // 6. Check MFA
        if (user.MfaEnabled)
        {
            if (string.IsNullOrEmpty(mfaCode))
            {
                // Return MFA challenge - no tokens yet
                return Result<LoginResponse>.Success(new LoginResponse
                {
                    MfaChallenge = true,
                    MfaMethod = "totp",
                    User = new UserDto(user.Id, user.Email, user.DisplayName),
                    Tenant = new TenantDto(currentTenant.Id, currentTenant.Subdomain, currentTenant.Name),
                });
            }

            // Inline MFA validation for single-shot login (client passes mfaCode with credentials)
            if (!string.IsNullOrEmpty(user.MfaSecret) && _totpService.ValidateCode(_mfaSecretProtector.Unprotect(user.MfaSecret), mfaCode))
            {
                // MFA valid, fall through to token issuance
                user.MfaFailedAttemptCount = 0;
            }
            else
            {
                // BUG-045: same atomic, retriable row-lock unit as the wrong-password path — the shared
                // failed-attempt counter increment and lockout decision must be atomic under concurrent MFA
                // failures. Hangfire email deferred until after commit.
                LockoutNotification? lockoutEmail = null;
                await RunFailedAttemptAsync(user, async () =>
                {
                    // FR-10: MFA failures count toward lockout threshold (shared counter via FailedLoginCount)
                    user.MfaFailedAttemptCount++;
                    user.FailedLoginCount++;

                    var maxAttempts = currentTenant.MaxFailedAttempts > 0 ? currentTenant.MaxFailedAttempts : 5;
                    if (user.FailedLoginCount >= maxAttempts)
                    {
                        var lockoutMinutes = currentTenant.LockoutDurationMinutes > 0 ? currentTenant.LockoutDurationMinutes : 15;
                        var effectiveLockoutMinutes = CalculateProgressiveLockoutMinutes(
                            user, lockoutMinutes, currentTenant.ProgressiveLockoutEnabled);

                        user.LockedUntil = DateTime.UtcNow.AddMinutes(effectiveLockoutMinutes);
                        user.LockoutCount++;
                        user.LastLockoutAt = DateTime.UtcNow;

                        await WriteAuditLogWithDetailAsync(user.Id, "account_locked", ipAddress, userAgent,
                            new { attemptCount = user.FailedLoginCount, lockedUntil = user.LockedUntil, source = "mfa_failure" },
                            cancellationToken);

                        lockoutEmail = new LockoutNotification(user.Email, user.DisplayName, user.LockedUntil!.Value, effectiveLockoutMinutes);
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await WriteAuditLogAsync(user.Id, "mfa_challenge_failure", ipAddress, userAgent, cancellationToken);
                }, cancellationToken);

                if (lockoutEmail is not null)
                {
                    _backgroundJobClient.Enqueue<ILockoutNotificationService>(
                        svc => svc.SendLockoutNotificationAsync(lockoutEmail.Email, lockoutEmail.DisplayName, lockoutEmail.LockedUntil, lockoutEmail.Minutes, default));
                }

                return Result<LoginResponse>.Failure("Invalid verification code.", 401);
            }
        }

        // 7. Issue tokens via shared helper
        return await IssueTokensAsync(user, currentTenant, userTenant, ipAddress, userAgent, cancellationToken);
    }

    public async Task<Result<RefreshTokenResponse>> RefreshTokenAsync(
        string refreshToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = _jwtService.HashToken(refreshToken);

        // Find the refresh token
        var storedToken = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        if (storedToken is null)
        {
            return Result<RefreshTokenResponse>.Failure("Invalid refresh token.", 401);
        }

        // ISSUE-049: a refresh token is bound to its owning tenant. Reject it when presented on a different
        // tenant's resolved subdomain, so a token minted for tenant A cannot be rotated under tenant B. Uses
        // the SAME generic message/status as an unknown token, so the response doesn't leak which check failed
        // (existence vs cross-tenant). Checked BEFORE reuse detection so a cross-tenant request can never
        // trigger lineage revocation against another tenant's tokens.
        if (_tenantContext.IsResolved && storedToken.TenantId != _tenantContext.TenantId)
        {
            return Result<RefreshTokenResponse>.Failure("Invalid refresh token.", 401);
        }

        // Check if token was already revoked (reuse detection)
        if (storedToken.RevokedAt is not null)
        {
            // BUG-043: token reuse detected — revoke only the COMPROMISED token's lineage (the chain that
            // descends from the reused token via ReplacedByTokenId), not every session for the user+tenant.
            // Independent sessions on other devices (separate chains) remain valid, so one stolen token no
            // longer logs the user out everywhere.
            _logger.LogWarning("Refresh token reuse detected for user {UserId}, tenant {TenantId}. Revoking the compromised token lineage.",
                storedToken.UserId, storedToken.TenantId);

            await RevokeTokenLineageAsync(storedToken, cancellationToken);

            // ISSUE-050: a detected reuse (a rotated/revoked token replayed) is a security event that belongs
            // in the queryable audit trail, not just a Serilog line. Actor = the token's owning user; scoped
            // to the token's tenant. Written after the lineage revocation commit (best-effort, additive).
            await WriteAuditLogAsync(storedToken.UserId, "security.refresh_token_reuse_detected",
                ipAddress, userAgent, cancellationToken, storedToken.TenantId);

            return Result<RefreshTokenResponse>.Failure("Token reuse detected. This session has been revoked.", 401);
        }

        // Check expiration
        if (storedToken.ExpiresAt <= DateTime.UtcNow)
        {
            return Result<RefreshTokenResponse>.Failure("Refresh token has expired.", 401);
        }

        // Check tenant status
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == storedToken.TenantId, cancellationToken);

        if (tenant is null || tenant.Status is TenantStatus.Suspended or TenantStatus.Terminated)
        {
            return Result<RefreshTokenResponse>.Failure(
                "This workspace is currently unavailable.", 403);
        }

        // Check user membership is still active
        var userTenant = await _dbContext.UserTenants
            .IgnoreQueryFilters()
            .Include(ut => ut.UserTenantRoles)
                .ThenInclude(utr => utr.Role)
                    .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(
                ut => ut.UserId == storedToken.UserId && ut.TenantId == storedToken.TenantId,
                cancellationToken);

        if (userTenant is null || userTenant.Status != UserTenantStatus.Active)
        {
            // Revoke remaining tokens
            await RevokeTokenChainAsync(storedToken.UserId, storedToken.TenantId, cancellationToken);
            return Result<RefreshTokenResponse>.Failure(
                "Your membership in this organization is no longer active.", 403);
        }

        // Check user is still globally active
        if (!storedToken.User.IsActive)
        {
            return Result<RefreshTokenResponse>.Failure("Account is inactive.", 401);
        }

        // Check idle timeout (US-AUTH-009 AC-2)
        if (storedToken.LastActiveAt.HasValue)
        {
            var idleMinutes = (DateTime.UtcNow - storedToken.LastActiveAt.Value).TotalMinutes;
            if (idleMinutes > tenant.IdleTimeoutMinutes)
            {
                storedToken.RevokedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await WriteAuditLogAsync(storedToken.UserId, "session_expired_idle", ipAddress, userAgent, cancellationToken, storedToken.TenantId);
                return Result<RefreshTokenResponse>.Failure("Session expired due to inactivity.", 401);
            }
        }

        // Check absolute timeout (US-AUTH-009 AC-3)
        var absoluteHours = (DateTime.UtcNow - storedToken.IssuedAt).TotalHours;
        if (absoluteHours > tenant.AbsoluteTimeoutHours)
        {
            storedToken.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await WriteAuditLogAsync(storedToken.UserId, "session_expired_absolute", ipAddress, userAgent, cancellationToken, storedToken.TenantId);
            return Result<RefreshTokenResponse>.Failure("Session expired. Please log in again.", 401);
        }

        // Rotate token
        storedToken.RevokedAt = DateTime.UtcNow;

        var roles = userTenant.UserTenantRoles.Select(utr => utr.Role.Name).ToList();
        var permissions = userTenant.UserTenantRoles
            .SelectMany(utr => utr.Role.RolePermissions)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToList();

        var newAccessToken = _jwtService.GenerateAccessToken(storedToken.User, storedToken.TenantId, userTenant.Id, roles, permissions);
        var newRawRefreshToken = _jwtService.GenerateRefreshToken();
        var newTokenHash = _jwtService.HashToken(newRawRefreshToken);

        var newRefreshTokenEntity = new RefreshToken
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = storedToken.UserId,
            TenantId = storedToken.TenantId,
            TokenHash = newTokenHash,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            UserAgent = userAgent ?? storedToken.UserAgent,
            IpAddress = ipAddress ?? storedToken.IpAddress,
            LastActiveAt = DateTime.UtcNow,
        };

        storedToken.ReplacedByTokenId = newRefreshTokenEntity.Id;
        _dbContext.RefreshTokens.Add(newRefreshTokenEntity);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Token refreshed for user {UserId}, tenant {TenantId}", storedToken.UserId, storedToken.TenantId);

        return Result<RefreshTokenResponse>.Success(new RefreshTokenResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRawRefreshToken,
        });
    }

    public async Task<Result> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return Result.Success(); // No token to revoke
        }

        var tokenHash = _jwtService.HashToken(refreshToken);

        var storedToken = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        if (storedToken is not null && storedToken.RevokedAt is null)
        {
            storedToken.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // BUG-039 (US-AUTH-003 FR-4/AC-1/NFR-4): audit the logout as a security event. The token row is
            // authoritative for the subject + tenant (logout may run without a resolved tenant context); no
            // ip/userAgent is threaded into this seam, so pass null as the existing session_revoked_* audits do.
            await WriteAuditLogAsync(storedToken.UserId, "logout", null, null, cancellationToken, storedToken.TenantId);

            _logger.LogInformation("User {UserId} logged out from tenant {TenantId}", storedToken.UserId, storedToken.TenantId);
        }

        return Result.Success();
    }

    public async Task<Result> ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        // Always return success to prevent user enumeration
        email = email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is not null && _tenantContext.IsResolved)
        {
            // Check membership in current tenant
            var hasMembership = await _dbContext.UserTenants
                .IgnoreQueryFilters()
                .AnyAsync(
                    ut => ut.UserId == user.Id && ut.TenantId == _tenantContext.TenantId && ut.Status == UserTenantStatus.Active,
                    cancellationToken);

            if (hasMembership)
            {
                // BUG-040: issue a real single-use, time-limited reset token. Only the SHA-256 hash is
                // stored; the raw token is delivered to the user out-of-band (email). Issuing a fresh
                // token invalidates any prior pending one for this user.
                var rawToken = GenerateResetToken();
                user.PasswordResetTokenHash = HashResetToken(rawToken);
                user.PasswordResetTokenExpiresAt = DateTime.UtcNow.Add(ResetTokenLifetime);
                await _dbContext.SaveChangesAsync(cancellationToken);

                // US-NTF-006 Phase 2b: dispatch the real password-reset email (SecurityAlerts, mandatory). The raw
                // token is delivered out-of-band via the reset link only — never logged (it is a credential) and
                // never returned in the API response (no-enumeration). A delivery failure must NOT fail the reset
                // request, so the dispatch is wrapped (the dispatcher itself is also never-throw).
                await DispatchPasswordResetEmailAsync(user, rawToken, cancellationToken);

                _logger.LogInformation(
                    "Password reset token issued for user {UserId} in tenant {TenantId}, expires {Expiry:o}.",
                    user.Id, _tenantContext.TenantId, user.PasswordResetTokenExpiresAt);
            }
        }

        // ISSUE-051 (US-AUTH-004 FR-8): audit the reset REQUEST event. Written for every request — including an
        // unknown email — so the security trail is complete, but the subject is the resolved user id or null
        // when the email doesn't map to a user; the row is server-side only and never surfaces existence to the
        // caller (the response is unconditionally success, matching the no-enumeration contract above).
        await WriteAuditLogAsync(user?.Id, "password_reset_requested", null, null, cancellationToken);

        // Always return success regardless of whether user exists
        return Result.Success();
    }

    /// <summary>Lifetime of a password-reset token (BUG-040).</summary>
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);

    /// <summary>Generates a 256-bit cryptographically-random, URL-safe reset token (BUG-040).</summary>
    private static string GenerateResetToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>SHA-256 hex of a reset token — what we store/compare, never the raw token (BUG-040).</summary>
    private static string HashResetToken(string rawToken)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// US-NTF-006 Phase 2b: dispatches the self-service password-reset email via <see cref="INotificationDispatcher"/>
    /// (catalog event <c>password_reset</c>, SecurityAlerts/mandatory). The reset link embeds the RAW token and is the
    /// only place it is surfaced; the token is never logged and never returned in the API response (no-enumeration).
    /// Wrapped so a delivery failure never fails the reset request; skips gracefully when no dispatcher is injected.
    /// </summary>
    private async Task DispatchPasswordResetEmailAsync(User user, string rawToken, CancellationToken cancellationToken)
    {
        if (_notificationDispatcher is null)
            return;

        try
        {
            var baseDomain = (_configuration["Platform:BaseDomain"] ?? "yourhrm.com").Trim().TrimStart('.');
            var subdomain = _tenantContext.Subdomain;
            var resetUrl = $"https://{subdomain}.{baseDomain}/reset-password?token={rawToken}";
            var expiryHours = (int)Math.Ceiling(ResetTokenLifetime.TotalHours);

            var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["user"] = new Dictionary<string, object?>
                {
                    // The password_reset template greets with {{user.firstName}}; the global User has only a
                    // DisplayName, so use it as the friendly name (empty → BR-5 renders as blank).
                    ["firstName"] = user.DisplayName,
                    ["email"] = user.Email,
                },
                ["reset"] = new Dictionary<string, object?>
                {
                    ["url"] = resetUrl,
                    ["expiryHours"] = expiryHours,
                },
            });

            var request = new NotificationRequest(
                TenantId: _tenantContext.TenantId,
                EventKey: "password_reset",
                PayloadJson: payloadJson,
                RecipientUserId: user.Id,
                NotificationType: "password.reset.requested");

            await _notificationDispatcher.SendEmailAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never fail the reset request on a delivery error — the token is already persisted and the response
            // stays an unconditional success (no-enumeration). The token is NOT included in the log.
            _logger.LogError(ex,
                "Password reset email dispatch failed for user {UserId} in tenant {TenantId}; reset still succeeded.",
                user.Id, _tenantContext.TenantId);
        }
    }

    public async Task<Result> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        email = email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            return Result.Failure("The reset link is invalid or has expired. Please request a new one.", 400);
        }

        // BUG-040: validate the reset token — it must be present, the user must have a pending token,
        // it must not be expired, and its hash must match. Anything else is rejected with the same
        // generic message (no oracle). A missing stored hash means no reset was requested.
        if (string.IsNullOrEmpty(token) ||
            string.IsNullOrEmpty(user.PasswordResetTokenHash) ||
            user.PasswordResetTokenExpiresAt is null ||
            user.PasswordResetTokenExpiresAt.Value <= DateTime.UtcNow)
        {
            return Result.Failure("The reset link is invalid or has expired. Please request a new one.", 400);
        }

        var providedHash = HashResetToken(token);
        var matches = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(providedHash),
            System.Text.Encoding.UTF8.GetBytes(user.PasswordResetTokenHash));
        if (!matches)
        {
            return Result.Failure("The reset link is invalid or has expired. Please request a new one.", 400);
        }

        // BUG-004: enforce the TENANT's configured password policy (min length, complexity), not just the
        // hardcoded validator defaults. Validated BEFORE the token is consumed so a policy failure lets the
        // user retry with the same link.
        var policyTenant = await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => new
            {
                t.MinPasswordLength, t.RequireUppercase, t.RequireLowercase, t.RequireDigit,
                t.RequireSpecialCharacter, t.PasswordHistoryCount, t.PasswordMaxAgeDays,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (policyTenant is not null)
        {
            var policy = new PasswordPolicy(
                policyTenant.MinPasswordLength, policyTenant.RequireUppercase, policyTenant.RequireLowercase,
                policyTenant.RequireDigit, policyTenant.RequireSpecialCharacter,
                policyTenant.PasswordHistoryCount, policyTenant.PasswordMaxAgeDays);
            var violations = PasswordPolicyValidator.Validate(newPassword, policy);
            if (violations.Count > 0)
                return Result.Failure(string.Join(" ", violations), 400);
        }

        // US-AUTH-004 FR-5 (ISSUE-053): password-history enforcement — reject reuse of the last N passwords.
        // Runs after policy validation and BEFORE the token is consumed, so a rejection lets the user retry
        // with the same link. historyCount <= 0 (disabled) skips the check entirely.
        var historyCount = policyTenant?.PasswordHistoryCount ?? 0;
        if (historyCount > 0)
        {
            var recentHashes = await _dbContext.PasswordHistories
                .IgnoreQueryFilters()
                .Where(ph => ph.UserId == user.Id)
                .OrderByDescending(ph => ph.CreatedAt)
                .ThenByDescending(ph => ph.Id)
                .Take(historyCount)
                .Select(ph => ph.PasswordHash)
                .ToListAsync(cancellationToken);

            // Seed the CURRENT password into the comparison so it counts even before it is recorded in history.
            var priorHashes = new List<string?>(recentHashes) { user.PasswordHash };

            if (PasswordHistoryValidator.IsReused(newPassword, priorHashes, (pw, hash) => BCrypt.Net.BCrypt.Verify(pw, hash)))
                return Result.Failure("You cannot reuse a recent password.", 400, "password_reused");
        }

        // Single-use: consume the token so it cannot be replayed.
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;

        // Hash and set new password (BR-2: password reset clears lockout)
        var previousHash = user.PasswordHash;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        user.PasswordChangedAt = DateTime.UtcNow;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.MfaFailedAttemptCount = 0;
        user.LockoutCount = 0;
        user.LastLockoutAt = null;

        // Revoke all refresh tokens across all tenants
        var allTokens = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var rt in allTokens)
        {
            rt.RevokedAt = DateTime.UtcNow;
        }

        // US-AUTH-004 FR-5 (ISSUE-053): record the new password in history (seeding the prior password on the
        // first change so it counts), then prune to the newest N below. Skipped when history is disabled.
        if (historyCount > 0)
        {
            var recordedAt = DateTime.UtcNow;

            var hasHistory = await _dbContext.PasswordHistories
                .IgnoreQueryFilters()
                .AnyAsync(ph => ph.UserId == user.Id, cancellationToken);

            if (!hasHistory && !string.IsNullOrEmpty(previousHash))
            {
                _dbContext.PasswordHistories.Add(new PasswordHistory
                {
                    Id = BaseEntity.NewUuidV7(),
                    UserId = user.Id,
                    PasswordHash = previousHash,
                    CreatedAt = recordedAt.AddMilliseconds(-1), // orders strictly before the new entry
                });
            }

            _dbContext.PasswordHistories.Add(new PasswordHistory
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = user.Id,
                PasswordHash = user.PasswordHash,
                CreatedAt = recordedAt,
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Prune password history beyond the configured count (keep the newest N entries).
        if (historyCount > 0)
        {
            var stale = await _dbContext.PasswordHistories
                .IgnoreQueryFilters()
                .Where(ph => ph.UserId == user.Id)
                .OrderByDescending(ph => ph.CreatedAt)
                .ThenByDescending(ph => ph.Id)
                .Skip(historyCount)
                .ToListAsync(cancellationToken);

            if (stale.Count > 0)
            {
                _dbContext.PasswordHistories.RemoveRange(stale);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        // ISSUE-051 (US-AUTH-004 FR-8): audit the successful reset completion. Reached only after the reset
        // token is validated, so the user id is a real, authorized subject; no ip/userAgent is threaded into
        // this seam, so pass null as the other credential-management audits do.
        await WriteAuditLogAsync(user.Id, "password_reset_completed", null, null, cancellationToken);

        _logger.LogInformation("Password reset completed for user {UserId}", user.Id);

        return Result.Success();
    }

    public async Task<Result> RevokeAllSessionsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tokens = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => rt.UserId == userId
                && rt.TenantId == tenantId
                && rt.RevokedAt == null
                && rt.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAt = DateTime.UtcNow;
        }

        if (tokens.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await WriteAuditLogAsync(userId, "session_revoked_by_admin", null, null, cancellationToken, tenantId);
        }

        _logger.LogInformation("All sessions revoked ({Count}) for user {UserId} in tenant {TenantId}", tokens.Count, userId, tenantId);

        return Result.Success();
    }

    public async Task<Result<CurrentUserDto>> GetCurrentUserAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result<CurrentUserDto>.Failure("User not found.", 404);
        }

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            return Result<CurrentUserDto>.Failure("Tenant not found.", 404);
        }

        var userTenant = await _dbContext.UserTenants
            .IgnoreQueryFilters()
            .Include(ut => ut.UserTenantRoles)
                .ThenInclude(utr => utr.Role)
                    .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(
                ut => ut.UserId == userId && ut.TenantId == tenantId,
                cancellationToken);

        if (userTenant is null)
        {
            return Result<CurrentUserDto>.Failure("User membership not found.", 404);
        }

        var roles = userTenant.UserTenantRoles.Select(utr => utr.Role.Name).ToList();
        var permissions = userTenant.UserTenantRoles
            .SelectMany(utr => utr.Role.RolePermissions)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToList();
        var myTenants = await GetMyTenantsAsync(userId, tenantId, cancellationToken);

        return Result<CurrentUserDto>.Success(new CurrentUserDto
        {
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Tenant = new TenantDto(tenant.Id, tenant.Subdomain, tenant.Name),
            Roles = roles,
            Permissions = permissions,
            TenantMemberships = myTenants.Value ?? [],
            MfaEnabled = user.MfaEnabled,
        });
    }

    public async Task<Result<IReadOnlyList<TenantMembershipDto>>> GetMyTenantsAsync(
        Guid userId,
        Guid currentTenantId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = MyTenantsCacheKey.For(userId); // BUG-116: one shared key source for read/write/invalidate

        // BUG-121: the cache is best-effort. A Redis outage must NOT 500 the FE-hydration path
        // (/auth/me, /auth/my-tenants) — treat a cache failure as a miss and fall back to the DB
        // (mirrors the fail-soft cache handling in TenantResolutionMiddleware).
        string? cached = null;
        try
        {
            cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "my-tenants cache read failed for user {UserId}; falling back to database", userId);
        }

        if (!string.IsNullOrWhiteSpace(cached))
        {
            var cachedMemberships = JsonSerializer.Deserialize<List<TenantMembershipDto>>(cached);
            if (cachedMemberships is not null)
            {
                return Result<IReadOnlyList<TenantMembershipDto>>.Success(
                    WithCurrentTenant(cachedMemberships, currentTenantId));
            }
        }

        var userExists = await _dbContext.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken);

        if (!userExists)
        {
            return Result<IReadOnlyList<TenantMembershipDto>>.Failure("User not found.", 404);
        }

        var userTenants = await _dbContext.UserTenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(ut => ut.Tenant)
            .Include(ut => ut.UserTenantRoles)
                .ThenInclude(utr => utr.Role)
            .Where(ut => ut.UserId == userId)
            .OrderBy(ut => ut.Tenant.Name)
            .ToListAsync(cancellationToken);

        var memberships = userTenants
            .Select(ut => new TenantMembershipDto(
                ut.TenantId,
                ut.Tenant.Subdomain,
                ut.Tenant.Name,
                ut.Tenant.LogoUrl,
                ut.Tenant.Status.ToString(),
                ut.UserTenantRoles
                    .Select(utr => utr.Role.Name)
                    .OrderBy(role => role)
                    .ToList(),
                false))
            .ToList();

        // BUG-121: cache write is best-effort too — a Redis outage must not fail the request.
        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(memberships),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "my-tenants cache write failed for user {UserId}; continuing without cache", userId);
        }

        return Result<IReadOnlyList<TenantMembershipDto>>.Success(
            WithCurrentTenant(memberships, currentTenantId));
    }

    public async Task<Result<SwitchTenantResponse>> SwitchTenantAsync(
        Guid userId,
        Guid sourceTenantId,
        Guid targetTenantId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return Result<SwitchTenantResponse>.Failure("Account is inactive.", 401);
        }

        if (user.IsLockedOut)
        {
            return Result<SwitchTenantResponse>.Failure(
                "Account temporarily locked. Try again later or contact your administrator.",
                401);
        }

        var targetTenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == targetTenantId, cancellationToken);

        if (targetTenant is null)
        {
            // ISSUE-055 (US-AUTH-008): a DENIED switch must leave a security-audit trail, not just succeed silently.
            await WriteTenantSwitchDeniedAuditAsync(
                userId, sourceTenantId, targetTenantId, "target_tenant_not_found", ipAddress, userAgent, cancellationToken);
            return Result<SwitchTenantResponse>.Failure(
                "You do not have an active membership in this organization.",
                403);
        }

        if (targetTenant.Status is not (TenantStatus.Active or TenantStatus.Trial))
        {
            // ISSUE-055 (US-AUTH-008): audit the denial (target tenant not in an accessible state).
            await WriteTenantSwitchDeniedAuditAsync(
                userId, sourceTenantId, targetTenantId, $"target_tenant_{targetTenant.Status}", ipAddress, userAgent, cancellationToken);
            return Result<SwitchTenantResponse>.Failure(
                $"The target organization is unavailable ({targetTenant.Status}).",
                403);
        }

        var userTenant = await _dbContext.UserTenants
            .IgnoreQueryFilters()
            .Include(ut => ut.UserTenantRoles)
                .ThenInclude(utr => utr.Role)
                    .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(
                ut => ut.UserId == userId && ut.TenantId == targetTenantId,
                cancellationToken);

        if (userTenant is null || userTenant.Status != UserTenantStatus.Active)
        {
            // ISSUE-055 (US-AUTH-008): audit the denial (no active membership in the target tenant — non-member).
            await WriteTenantSwitchDeniedAuditAsync(
                userId, sourceTenantId, targetTenantId, "not_a_member", ipAddress, userAgent, cancellationToken);
            return Result<SwitchTenantResponse>.Failure(
                "You do not have an active membership in this organization.",
                403);
        }

        if (!user.MfaEnabled
            && targetTenant.MfaPolicy == "required"
            && userTenant.UserTenantRoles.Any(utr =>
                targetTenant.MfaRequiredRoles.Contains(utr.Role.Name)))
        {
            // ISSUE-055 (US-AUTH-008): audit the denial (MFA enrollment required by target-tenant policy).
            await WriteTenantSwitchDeniedAuditAsync(
                userId, sourceTenantId, targetTenantId, "mfa_enrollment_required", ipAddress, userAgent, cancellationToken);
            return Result<SwitchTenantResponse>.Failure(
                "MFA enrollment is required before accessing this organization.",
                403);
        }

        var tokenResult = await IssueTokensAsync(
            user,
            targetTenant,
            userTenant,
            ipAddress,
            userAgent,
            cancellationToken);

        if (tokenResult.IsFailure)
        {
            return Result<SwitchTenantResponse>.Failure(
                tokenResult.Error!,
                tokenResult.StatusCode ?? 400);
        }

        await WriteTenantSwitchAuditAsync(
            userId,
            sourceTenantId,
            targetTenantId,
            ipAddress,
            userAgent,
            cancellationToken);

        _logger.LogInformation(
            "User {UserId} switched tenant from {SourceTenantId} to {TargetTenantId}",
            userId,
            sourceTenantId,
            targetTenantId);

        var loginResponse = tokenResult.Value!;
        return Result<SwitchTenantResponse>.Success(new SwitchTenantResponse
        {
            AccessToken = loginResponse.AccessToken,
            RefreshToken = loginResponse.RefreshToken,
            Tenant = new TenantDto(targetTenant.Id, targetTenant.Subdomain, targetTenant.Name),
            RedirectUrl = BuildTenantRedirectUrl(targetTenant.Subdomain),
        });
    }

    #region MFA Operations

    public async Task<Result<MfaEnrollResponse>> EnrollMfaAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result<MfaEnrollResponse>.Failure("User not found.", 404);
        }

        if (user.MfaEnabled)
        {
            return Result<MfaEnrollResponse>.Failure("MFA is already enabled.", 409);
        }

        // Generate TOTP secret
        var secret = _totpService.GenerateSecret();

        // US-AUTH-005 NFR-2: encrypt the TOTP secret at rest (ASP.NET Core Data Protection) — a raw DB read
        // must not disclose it. Read sites decrypt via _mfaSecretProtector.Unprotect (legacy-plaintext safe).
        user.MfaSecret = _mfaSecretProtector.Protect(secret);
        user.MfaEnabled = false; // Only flips to true after verify

        // Build otpauth URI
        var otpAuthUri = _totpService.GenerateOtpAuthUri(secret, user.Email, "HRM");

        // Generate QR code data URL (server-side, NFR-5)
        var qrCodeDataUrl = _totpService.GenerateQrCodeDataUrl(otpAuthUri);

        // Generate 10 recovery codes (NFR-3: shown once only)
        var recoveryCodes = _totpService.GenerateRecoveryCodes(10);

        // Delete any pre-existing recovery codes for this user (re-enrollment scenario)
        var existingCodes = await _dbContext.MfaRecoveryCodes
            .IgnoreQueryFilters()
            .Where(rc => rc.UserId == userId)
            .ToListAsync(cancellationToken);

        if (existingCodes.Count > 0)
        {
            _dbContext.MfaRecoveryCodes.RemoveRange(existingCodes);
        }

        // Insert new recovery code rows with hashed values
        foreach (var code in recoveryCodes)
        {
            _dbContext.MfaRecoveryCodes.Add(new MfaRecoveryCode
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = userId,
                CodeHash = _totpService.HashRecoveryCode(code),
                UsedAt = null,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Audit log
        await WriteAuditLogAsync(userId, "mfa_enroll_initiated", null, null, cancellationToken);

        _logger.LogInformation("MFA enrollment initiated for user {UserId}", userId);

        return Result<MfaEnrollResponse>.Success(new MfaEnrollResponse
        {
            Secret = secret,
            QrCodeDataUrl = qrCodeDataUrl,
            RecoveryCodes = recoveryCodes,
        });
    }

    public async Task<Result<MfaVerifyResponse>> VerifyMfaEnrollmentAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || string.IsNullOrEmpty(user.MfaSecret))
        {
            return Result<MfaVerifyResponse>.Failure("User not found or MFA not initiated.", 404);
        }

        if (user.MfaEnabled)
        {
            return Result<MfaVerifyResponse>.Failure("MFA is already enabled.", 409);
        }

        // Validate code
        if (!_totpService.ValidateCode(_mfaSecretProtector.Unprotect(user.MfaSecret), code))
        {
            // BUG-045: same lost-update class — increment the failed-attempt counter atomically under a
            // per-user row lock (as one retriable unit) so parallel enrollment-verify failures cannot lose
            // increments. No lockout side-effects here, so no deferred Hangfire email.
            await RunFailedAttemptAsync(user, async () =>
            {
                user.MfaFailedAttemptCount++;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await WriteAuditLogAsync(userId, "mfa_challenge_failure", null, null, cancellationToken);
            }, cancellationToken);

            _logger.LogWarning("MFA enrollment verification failed for user {UserId}, attempt {Attempt}",
                userId, user.MfaFailedAttemptCount);

            return Result<MfaVerifyResponse>.Failure("Invalid verification code.", 401);
        }

        // Success: enable MFA
        user.MfaEnabled = true;
        user.MfaFailedAttemptCount = 0;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await WriteAuditLogAsync(userId, "mfa_enrolled", null, null, cancellationToken);

        _logger.LogInformation("MFA enrolled successfully for user {UserId}", userId);

        // Recovery codes were already returned during enrollment (NFR-3)
        return Result<MfaVerifyResponse>.Success(new MfaVerifyResponse
        {
            Success = true,
            RecoveryCodes = null,
        });
    }

    public async Task<Result<LoginResponse>> VerifyMfaLoginAsync(
        string email,
        string code,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        email = email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null || !user.MfaEnabled)
        {
            return Result<LoginResponse>.Failure("Invalid credentials.", 401);
        }

        // FR-5: Check lockout BEFORE verifying MFA code
        if (user.LockedUntil.HasValue)
        {
            if (user.LockedUntil.Value > DateTime.UtcNow)
            {
                return Result<LoginResponse>.Failure(
                    "Account temporarily locked. Try again later or contact your administrator.", 401);
            }

            // Lockout expired -- clear state (AC-4)
            user.LockedUntil = null;
            user.FailedLoginCount = 0;
            user.MfaFailedAttemptCount = 0;
            await WriteAuditLogAsync(user.Id, "account_unlocked_by_timeout", ipAddress, userAgent, cancellationToken);
        }

        var codeIsValid = false;

        // Try TOTP validation first
        if (!string.IsNullOrEmpty(user.MfaSecret) && _totpService.ValidateCode(_mfaSecretProtector.Unprotect(user.MfaSecret), code))
        {
            codeIsValid = true;
        }

        // If TOTP fails and code looks like a recovery code (8+ chars), check recovery codes
        if (!codeIsValid)
        {
            var stripped = code.Replace("-", "");
            if (stripped.Length >= 8)
            {
                var unusedCodes = await _dbContext.MfaRecoveryCodes
                    .IgnoreQueryFilters()
                    .Where(rc => rc.UserId == user.Id && rc.UsedAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var rc in unusedCodes)
                {
                    if (_totpService.VerifyRecoveryCode(code, rc.CodeHash))
                    {
                        rc.UsedAt = DateTime.UtcNow;
                        codeIsValid = true;
                        await WriteAuditLogAsync(user.Id, "mfa_recovery_code_used", ipAddress, userAgent, cancellationToken);
                        _logger.LogInformation("Recovery code used for user {UserId}", user.Id);
                        break;
                    }
                }
            }
        }

        if (!codeIsValid)
        {
            // BUG-045: run the shared failed-attempt counter increment + lockout decision as one atomic,
            // retriable unit under a per-user row lock, decided from the authoritative post-increment count
            // (no lost-update). Hangfire email deferred until after commit.
            LockoutNotification? lockoutEmail = null;
            await RunFailedAttemptAsync(user, async () =>
            {
                // FR-10: MFA failures count toward lockout threshold (shared counter)
                user.MfaFailedAttemptCount++;
                user.FailedLoginCount++;

                // Determine lockout policy from tenant
                var (maxAttempts, lockoutMinutes, progressiveLockoutEnabled) = await GetLockoutPolicyAsync(cancellationToken);

                // Audit: login_failure
                await WriteAuditLogWithDetailAsync(user.Id, "login_failure", ipAddress, userAgent,
                    new { attemptCount = user.FailedLoginCount, source = "mfa" },
                    cancellationToken);

                if (user.FailedLoginCount >= maxAttempts)
                {
                    var effectiveLockoutMinutes = CalculateProgressiveLockoutMinutes(
                        user, lockoutMinutes, progressiveLockoutEnabled);

                    user.LockedUntil = DateTime.UtcNow.AddMinutes(effectiveLockoutMinutes);
                    user.LockoutCount++;
                    user.LastLockoutAt = DateTime.UtcNow;

                    _logger.LogWarning("Account locked for user {UserId} after {Attempts} failed MFA attempts. Duration: {Duration}m",
                        user.Id, user.FailedLoginCount, effectiveLockoutMinutes);

                    await WriteAuditLogWithDetailAsync(user.Id, "account_locked", ipAddress, userAgent,
                        new { attemptCount = user.FailedLoginCount, lockedUntil = user.LockedUntil, source = "mfa_failure" },
                        cancellationToken);

                    lockoutEmail = new LockoutNotification(user.Email, user.DisplayName, user.LockedUntil!.Value, effectiveLockoutMinutes);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await WriteAuditLogAsync(user.Id, "mfa_challenge_failure", ipAddress, userAgent, cancellationToken);
            }, cancellationToken);

            if (lockoutEmail is not null)
            {
                _backgroundJobClient.Enqueue<ILockoutNotificationService>(
                    svc => svc.SendLockoutNotificationAsync(lockoutEmail.Email, lockoutEmail.DisplayName, lockoutEmail.LockedUntil, lockoutEmail.Minutes, default));
            }

            return Result<LoginResponse>.Failure("Invalid verification code.", 401);
        }

        // MFA success - reset counters
        user.MfaFailedAttemptCount = 0;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await WriteAuditLogAsync(user.Id, "mfa_challenge_success", ipAddress, userAgent, cancellationToken);

        // Issue tokens via shared helper
        if (!_tenantContext.IsResolved)
        {
            return Result<LoginResponse>.Failure("Tenant context is not resolved.", 400);
        }

        var currentTenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, cancellationToken);

        if (currentTenant is null)
        {
            return Result<LoginResponse>.Failure("Tenant not found.", 404);
        }

        if (currentTenant.Status is TenantStatus.Suspended or TenantStatus.Terminated)
        {
            return Result<LoginResponse>.Failure(
                "This workspace is currently unavailable. Please contact support.", 403);
        }

        var userTenant = await _dbContext.UserTenants
            .IgnoreQueryFilters()
            .Include(ut => ut.UserTenantRoles)
                .ThenInclude(utr => utr.Role)
                    .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(
                ut => ut.UserId == user.Id && ut.TenantId == _tenantContext.TenantId,
                cancellationToken);

        if (userTenant is null || userTenant.Status != UserTenantStatus.Active)
        {
            return Result<LoginResponse>.Failure(
                "You do not have an active membership in this organization.", 403);
        }

        return await IssueTokensAsync(user, currentTenant, userTenant, ipAddress, userAgent, cancellationToken);
    }

    public async Task<Result> DisableMfaAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.MfaEnabled)
        {
            return Result.Failure("User not found or MFA is not enabled.", 404);
        }

        // Check tenant policy (BR-3)
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is not null && tenant.MfaPolicy == "required")
        {
            var userTenant = await _dbContext.UserTenants
                .IgnoreQueryFilters()
                .Include(ut => ut.UserTenantRoles)
                    .ThenInclude(utr => utr.Role)
                .FirstOrDefaultAsync(
                    ut => ut.UserId == userId && ut.TenantId == tenantId,
                    cancellationToken);

            if (userTenant is not null &&
                userTenant.UserTenantRoles.Any(utr =>
                    tenant.MfaRequiredRoles.Contains(utr.Role.Name)))
            {
                return Result.Failure(
                    "MFA is required by tenant policy for your role and cannot be disabled.", 403);
            }
        }

        // Disable MFA
        user.MfaEnabled = false;
        user.MfaSecret = null;
        user.MfaFailedAttemptCount = 0;

        // Delete all recovery codes for this user
        var recoveryCodes = await _dbContext.MfaRecoveryCodes
            .IgnoreQueryFilters()
            .Where(rc => rc.UserId == userId)
            .ToListAsync(cancellationToken);

        if (recoveryCodes.Count > 0)
        {
            _dbContext.MfaRecoveryCodes.RemoveRange(recoveryCodes);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await WriteAuditLogAsync(userId, "mfa_disabled", null, null, cancellationToken);

        _logger.LogInformation("MFA disabled for user {UserId}", userId);

        return Result.Success();
    }

    public async Task<Result<TenantAuthSettingsResponse>> GetTenantAuthSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            return Result<TenantAuthSettingsResponse>.Failure("Tenant not found.", 404);
        }

        return Result<TenantAuthSettingsResponse>.Success(new TenantAuthSettingsResponse
        {
            MfaPolicy = tenant.MfaPolicy,
            MfaRequiredRoles = tenant.MfaRequiredRoles ?? [],
            IdleTimeoutMinutes = tenant.IdleTimeoutMinutes,
            AbsoluteTimeoutHours = tenant.AbsoluteTimeoutHours,
            MaxConcurrentSessions = tenant.MaxConcurrentSessions,
            ConcurrentSessionStrategy = tenant.ConcurrentSessionStrategy,
            // US-AUTH-010 FR-3: Lockout policy settings
            MaxFailedAttempts = tenant.MaxFailedAttempts,
            LockoutDurationMinutes = tenant.LockoutDurationMinutes,
            ProgressiveLockoutEnabled = tenant.ProgressiveLockoutEnabled,
        });
    }

    public async Task<Result> UpdateTenantAuthSettingsAsync(
        Guid tenantId,
        TenantAuthSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var validPolicies = new[] { "off", "optional", "required" };
        if (!validPolicies.Contains(request.MfaPolicy))
        {
            return Result.Failure("MFA policy must be one of: off, optional, required.", 400);
        }

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure("Tenant not found.", 404);
        }

        tenant.MfaPolicy = request.MfaPolicy;
        tenant.MfaRequiredRoles = request.MfaRequiredRoles ?? [];

        // Session policy settings (US-AUTH-009 FR-1)
        if (request.IdleTimeoutMinutes.HasValue)
        {
            if (request.IdleTimeoutMinutes.Value < 1 || request.IdleTimeoutMinutes.Value > 1440)
                return Result.Failure("Idle timeout must be between 1 and 1440 minutes.", 400);
            tenant.IdleTimeoutMinutes = request.IdleTimeoutMinutes.Value;
        }

        if (request.AbsoluteTimeoutHours.HasValue)
        {
            if (request.AbsoluteTimeoutHours.Value < 1 || request.AbsoluteTimeoutHours.Value > 720)
                return Result.Failure("Absolute timeout must be between 1 and 720 hours.", 400);
            tenant.AbsoluteTimeoutHours = request.AbsoluteTimeoutHours.Value;
        }

        if (request.MaxConcurrentSessions.HasValue)
        {
            if (request.MaxConcurrentSessions.Value < 1 || request.MaxConcurrentSessions.Value > 100)
                return Result.Failure("Max concurrent sessions must be between 1 and 100.", 400);
            tenant.MaxConcurrentSessions = request.MaxConcurrentSessions.Value;
        }

        if (!string.IsNullOrEmpty(request.ConcurrentSessionStrategy))
        {
            var validStrategies = new[] { "deny_new", "revoke_oldest" };
            if (!validStrategies.Contains(request.ConcurrentSessionStrategy))
                return Result.Failure("Concurrent session strategy must be 'deny_new' or 'revoke_oldest'.", 400);
            tenant.ConcurrentSessionStrategy = request.ConcurrentSessionStrategy;
        }

        // Lockout policy settings (US-AUTH-010 FR-3, BR-5)
        if (request.MaxFailedAttempts.HasValue)
        {
            if (request.MaxFailedAttempts.Value < 3 || request.MaxFailedAttempts.Value > 10)
                return Result.Failure("Max failed attempts must be between 3 and 10.", 400);
            tenant.MaxFailedAttempts = request.MaxFailedAttempts.Value;
        }

        if (request.LockoutDurationMinutes.HasValue)
        {
            if (request.LockoutDurationMinutes.Value < 5 || request.LockoutDurationMinutes.Value > 60)
                return Result.Failure("Lockout duration must be between 5 and 60 minutes.", 400);
            tenant.LockoutDurationMinutes = request.LockoutDurationMinutes.Value;
        }

        if (request.ProgressiveLockoutEnabled.HasValue)
        {
            tenant.ProgressiveLockoutEnabled = request.ProgressiveLockoutEnabled.Value;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await WriteAuditLogAsync(null, "tenant_auth_settings_updated", null, null, cancellationToken, tenantId);

        _logger.LogInformation(
            "Tenant {TenantId} auth settings updated (MFA: {Policy}, Session: idle={Idle}m abs={Abs}h max={Max} strategy={Strategy}, Lockout: maxAttempts={MaxAttempts} duration={LockoutDuration}m progressive={Progressive})",
            tenantId, request.MfaPolicy, tenant.IdleTimeoutMinutes, tenant.AbsoluteTimeoutHours,
            tenant.MaxConcurrentSessions, tenant.ConcurrentSessionStrategy,
            tenant.MaxFailedAttempts, tenant.LockoutDurationMinutes, tenant.ProgressiveLockoutEnabled);

        return Result.Success();
    }

    #endregion

    #region Session Management (US-AUTH-009)

    public async Task<Result<IReadOnlyList<SessionDto>>> GetUserSessionsAsync(
        Guid userId,
        Guid tenantId,
        Guid? currentSessionId,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(rt => rt.UserId == userId
                && rt.TenantId == tenantId
                && rt.RevokedAt == null
                && rt.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(rt => rt.LastActiveAt ?? rt.IssuedAt)
            .ToListAsync(cancellationToken);

        var result = sessions.Select(rt =>
        {
            var (device, browser, os) = UserAgentParser.Parse(rt.UserAgent);
            return new SessionDto
            {
                SessionId = rt.Id,
                Device = device,
                Browser = browser,
                Os = os,
                IpAddress = rt.IpAddress,
                IssuedAt = rt.IssuedAt,
                LastActiveAt = rt.LastActiveAt,
                IsCurrent = currentSessionId.HasValue && rt.Id == currentSessionId.Value,
            };
        }).ToList();

        return Result<IReadOnlyList<SessionDto>>.Success(result);
    }

    public async Task<Result> RevokeSessionAsync(
        Guid sessionId,
        Guid userId,
        Guid tenantId,
        Guid? currentSessionId,
        bool isAdminAction,
        CancellationToken cancellationToken = default)
    {
        var token = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                rt => rt.Id == sessionId
                    && rt.UserId == userId
                    && rt.TenantId == tenantId
                    && rt.RevokedAt == null
                    && rt.ExpiresAt > DateTime.UtcNow,
                cancellationToken);

        if (token is null)
        {
            return Result.Failure("Session not found or already revoked.", 404);
        }

        // BR-4: User cannot revoke their own current session via self-service
        if (!isAdminAction && currentSessionId.HasValue && token.Id == currentSessionId.Value)
        {
            return Result.Failure("Cannot revoke the current session. Use the logout function instead.", 400);
        }

        token.RevokedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var eventType = isAdminAction ? "session_revoked_by_admin" : "session_revoked_by_user";
        // ISSUE-058 (US-AUTH-009): stamp the ACTING user as the audit actor. For an admin revoke that is the
        // acting admin (_currentUser), NOT the session owner (`userId`, the victim); the victim + revoked
        // session id are carried in the detail so the trail still records WHOSE session was revoked. The
        // self-revoke path keeps actor == owner (there _currentUser.UserId == userId anyway). If no current
        // user is resolved (isolated construction), fall back to `userId` so the row is still attributed.
        var actorUserId = isAdminAction && _currentUser?.IsAuthenticated == true
            ? _currentUser.UserId
            : userId;
        await WriteAuditLogWithDetailAsync(
            actorUserId, eventType, null, null,
            new { targetUserId = userId, sessionId },
            cancellationToken, tenantId);

        _logger.LogInformation("Session {SessionId} revoked for user {UserId} in tenant {TenantId} by {Actor}",
            sessionId, userId, tenantId, isAdminAction ? "admin" : "user");

        return Result.Success();
    }

    public async Task<Result> UpdateSessionActivityAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var token = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rt => rt.Id == sessionId && rt.RevokedAt == null, cancellationToken);

        if (token is not null)
        {
            token.LastActiveAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Guid?> GetSessionIdFromTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return null;

        var tokenHash = _jwtService.HashToken(refreshToken);
        var session = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(rt => rt.TokenHash == tokenHash && rt.RevokedAt == null)
            .Select(rt => (Guid?)rt.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return session;
    }

    #endregion

    #region Account Lockout Management (US-AUTH-010)

    public async Task<Result> UnlockUserAsync(
        Guid userId,
        Guid tenantId,
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        // BR-3: Admin may only unlock users with a membership in their tenant
        var userTenantMembership = await _dbContext.UserTenants
            .IgnoreQueryFilters()
            .AnyAsync(
                ut => ut.UserId == userId && ut.TenantId == tenantId,
                cancellationToken);

        if (!userTenantMembership)
        {
            return Result.Failure("User does not have a membership in your tenant.", 403);
        }

        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure("User not found.", 404);
        }

        // Clear lockout state (AC-5)
        user.LockedUntil = null;
        user.FailedLoginCount = 0;
        user.MfaFailedAttemptCount = 0;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Audit: account_unlocked_by_admin
        await WriteAuditLogWithDetailAsync(userId, "account_unlocked_by_admin", null, null,
            new { adminUserId },
            cancellationToken, tenantId);

        _logger.LogInformation("User {UserId} unlocked by admin {AdminUserId} in tenant {TenantId}",
            userId, adminUserId, tenantId);

        return Result.Success();
    }

    #endregion

    /// <summary>
    /// CR-AUTH-001 (US-AUTH-014): completes a Microsoft SSO login for an already-validated, already-
    /// isolation-checked <see cref="SsoIdentity"/>. The SSO callback runs with NO resolved tenant context
    /// (full-page redirect from Microsoft), so every query here is explicitly tenant-scoped via
    /// IgnoreQueryFilters + manual TenantId predicates.
    /// </summary>
    public async Task<Result<LoginResponse>> SsoSignInAsync(SsoIdentity identity, CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Subdomain == identity.Subdomain && !t.IsDeleted, cancellationToken);

        if (tenant is null)
        {
            _logger.LogWarning("SSO sign-in rejected: HRM tenant '{Subdomain}' not found.", identity.Subdomain);
            return Result<LoginResponse>.Failure("Workspace not found.", 404);
        }

        if (tenant.Status != TenantStatus.Active && tenant.Status != TenantStatus.Trial)
        {
            return Result<LoginResponse>.Failure("This workspace is not active.", 403);
        }

        var email = identity.Email.Trim().ToLowerInvariant();

        // 1) Match by Entra oid (primary), else by email. We defer writing the oid link until the login is
        //    fully authorized below, so a denied attempt never mutates the account.
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.EntraObjectId == identity.ObjectId, cancellationToken);

        var needsOidLink = false;
        if (user is null)
        {
            user = await _dbContext.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
            needsOidLink = user is not null;
        }

        // 2) No match → just-in-time provision (gated by the domain allow-list decided in the protocol layer).
        if (user is null)
        {
            if (!identity.JitAllowed)
            {
                await WriteAuditLogAsync(null, "sso_login_no_account", identity.IpAddress, identity.UserAgent, cancellationToken, tenant.Id);
                return Result<LoginResponse>.Failure("No HRM account is linked to this Microsoft identity.", 403);
            }

            user = new User
            {
                Id = BaseEntity.NewUuidV7(),
                Email = email,
                DisplayName = identity.DisplayName,
                PasswordHash = null,
                IsActive = true,
                IdentityProvider = "entra",
                EntraObjectId = identity.ObjectId,
                CreatedAt = DateTime.UtcNow,
            };
            _dbContext.Users.Add(user);
        }
        else if (!user.IsActive)
        {
            return Result<LoginResponse>.Failure("Your account is inactive.", 403);
        }

        // 3) Ensure an active tenant membership; JIT-create one (with the default role) when allowed.
        var userTenant = await _dbContext.UserTenants
            .IgnoreQueryFilters()
            .Include(ut => ut.UserTenantRoles).ThenInclude(utr => utr.Role).ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(ut => ut.UserId == user.Id && ut.TenantId == tenant.Id, cancellationToken);

        if (userTenant is null)
        {
            if (!identity.JitAllowed)
            {
                await WriteAuditLogAsync(user.Id, "sso_login_no_membership", identity.IpAddress, identity.UserAgent, cancellationToken, tenant.Id);
                return Result<LoginResponse>.Failure("You do not have access to this workspace.", 403);
            }

            var role = await _dbContext.Roles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Name == identity.DefaultRole, cancellationToken);

            if (role is null)
            {
                _logger.LogError("SSO JIT provisioning failed: default role '{Role}' missing for tenant {TenantId}.",
                    identity.DefaultRole, tenant.Id);
                return Result<LoginResponse>.Failure("SSO is misconfigured for this workspace (default role missing).", 500);
            }

            userTenant = new UserTenant
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = user.Id,
                TenantId = tenant.Id,
                Status = UserTenantStatus.Active,
                CreatedAt = DateTime.UtcNow,
            };
            userTenant.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = userTenant.Id,
                RoleId = role.Id,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = "sso-jit",
            });
            _dbContext.UserTenants.Add(userTenant);
            if (needsOidLink)
            {
                user.EntraObjectId = identity.ObjectId;
                user.IdentityProvider ??= "entra";
                user.UpdatedAt = DateTime.UtcNow;
                needsOidLink = false;
            }
            await _dbContext.SaveChangesAsync(cancellationToken);

            // BUG-116: a new membership was created — drop the user's cached my-tenants list so it isn't stale.
            if (_myTenantsCache is not null)
                await _myTenantsCache.InvalidateAsync(user.Id, cancellationToken);

            // Reload with the role graph populated (Role + RolePermissions) for token issuance.
            userTenant = await _dbContext.UserTenants
                .IgnoreQueryFilters()
                .Include(ut => ut.UserTenantRoles).ThenInclude(utr => utr.Role).ThenInclude(r => r.RolePermissions)
                .FirstAsync(ut => ut.Id == userTenant.Id, cancellationToken);

            _logger.LogInformation("SSO JIT-provisioned user {UserId} into tenant {TenantId} as {Role}.",
                user.Id, tenant.Id, identity.DefaultRole);
        }
        else if (userTenant.Status != UserTenantStatus.Active)
        {
            return Result<LoginResponse>.Failure("Your access to this workspace is not active.", 403);
        }

        // Authorized — link the Entra oid to the matched local account now (first SSO login).
        if (needsOidLink)
        {
            user.EntraObjectId = identity.ObjectId;
            user.IdentityProvider ??= "entra";
            user.UpdatedAt = DateTime.UtcNow;
        }

        await WriteAuditLogAsync(user.Id, "sso_login", identity.IpAddress, identity.UserAgent, cancellationToken, tenant.Id);

        return await IssueTokensAsync(user, tenant, userTenant, identity.IpAddress, identity.UserAgent, cancellationToken);
    }

    #region Private Helpers

    /// <summary>
    /// Shared helper for issuing access + refresh tokens after successful authentication.
    /// Called from both LoginAsync and VerifyMfaLoginAsync to avoid duplicating token issuance logic.
    /// </summary>
    private async Task<Result<LoginResponse>> IssueTokensAsync(
        User user,
        Tenant tenant,
        UserTenant userTenant,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        // Reset failed login count on success
        user.FailedLoginCount = 0;
        user.LockedUntil = null;

        // Gather roles and permissions
        var roles = userTenant.UserTenantRoles
            .Select(utr => utr.Role.Name)
            .ToList();

        var permissions = userTenant.UserTenantRoles
            .SelectMany(utr => utr.Role.RolePermissions)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToList();

        // Generate tokens
        var accessToken = _jwtService.GenerateAccessToken(user, tenant.Id, userTenant.Id, roles, permissions);
        var rawRefreshToken = _jwtService.GenerateRefreshToken();
        var refreshTokenHash = _jwtService.HashToken(rawRefreshToken);

        // Check concurrent session limits
        var activeSessions = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .CountAsync(
                rt => rt.UserId == user.Id
                    && rt.TenantId == tenant.Id
                    && rt.RevokedAt == null
                    && rt.ExpiresAt > DateTime.UtcNow,
                cancellationToken);

        if (activeSessions >= tenant.MaxConcurrentSessions)
        {
            if (tenant.ConcurrentSessionStrategy == "deny_new")
            {
                // US-AUTH-009 AC-1: Audit concurrent_session_denied
                await WriteAuditLogAsync(user.Id, "concurrent_session_denied", ipAddress, userAgent, cancellationToken, tenant.Id);
                return Result<LoginResponse>.Failure(
                    "Maximum concurrent sessions reached. Please log out from another device.", 403);
            }

            // Revoke oldest session (US-AUTH-009 AC-1: revoke_oldest strategy)
            var oldestSession = await _dbContext.RefreshTokens
                .IgnoreQueryFilters()
                .Where(rt => rt.UserId == user.Id
                    && rt.TenantId == tenant.Id
                    && rt.RevokedAt == null
                    && rt.ExpiresAt > DateTime.UtcNow)
                .OrderBy(rt => rt.IssuedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (oldestSession is not null)
            {
                oldestSession.RevokedAt = DateTime.UtcNow;
                await WriteAuditLogAsync(user.Id, "concurrent_session_oldest_revoked", ipAddress, userAgent, cancellationToken, tenant.Id);
            }
        }

        // Store refresh token
        var refreshTokenEntity = new RefreshToken
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = user.Id,
            TenantId = tenant.Id,
            TokenHash = refreshTokenHash,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            UserAgent = userAgent,
            IpAddress = ipAddress,
            LastActiveAt = DateTime.UtcNow,
        };

        _dbContext.RefreshTokens.Add(refreshTokenEntity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // ISSUE-048 (US-AUTH-001 FR-9): this is the single success exit for password, MFA, SSO, and
        // tenant-switch logins, so one audit row here covers every authenticated-login path. Actor + tenant +
        // ip/userAgent mirror the failure-path (login_failure/account_locked) audits.
        await WriteAuditLogAsync(user.Id, "login_success", ipAddress, userAgent, cancellationToken, tenant.Id);

        _logger.LogInformation("User {UserId} logged in to tenant {TenantId}", user.Id, tenant.Id);

        return Result<LoginResponse>.Success(new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            User = new UserDto(user.Id, user.Email, user.DisplayName),
            Tenant = new TenantDto(tenant.Id, tenant.Subdomain, tenant.Name),
            Permissions = permissions,
        });
    }

    private async Task RevokeTokenChainAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var tokens = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => rt.UserId == userId
                && rt.TenantId == tenantId
                && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// BUG-043: revokes ONLY the lineage (rotation chain) that the reused token belongs to — walking forward
    /// via <see cref="RefreshToken.ReplacedByTokenId"/> — instead of every session for the user+tenant. This
    /// kills the compromised session without logging the user out of unrelated concurrent sessions.
    /// </summary>
    private async Task RevokeTokenLineageAsync(RefreshToken reusedToken, CancellationToken cancellationToken)
    {
        var sameOwnerTokens = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => rt.UserId == reusedToken.UserId && rt.TenantId == reusedToken.TenantId)
            .ToListAsync(cancellationToken);
        var byId = sameOwnerTokens.ToDictionary(t => t.Id);

        var now = DateTime.UtcNow;
        // Walk forward from the reused token through its replacements; revoke the whole descendant chain.
        RefreshToken? current = byId.TryGetValue(reusedToken.Id, out var start) ? start : reusedToken;
        var guard = 0;
        while (current is not null && guard++ < sameOwnerTokens.Count + 1)
        {
            if (current.RevokedAt is null)
                current.RevokedAt = now;
            current = current.ReplacedByTokenId is { } nextId && byId.TryGetValue(nextId, out var next)
                ? next
                : null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<TenantMembershipDto> WithCurrentTenant(
        IEnumerable<TenantMembershipDto> memberships,
        Guid currentTenantId)
    {
        return memberships
            .Select(membership => membership with
            {
                IsCurrentTenant = membership.TenantId == currentTenantId,
            })
            .ToList();
    }

    private string BuildTenantRedirectUrl(string subdomain)
    {
        var baseDomain = (_configuration["Platform:BaseDomain"] ?? "yourhrm.com").Trim();
        var normalizedBaseDomain = baseDomain.TrimStart('.');

        return $"https://{subdomain}.{normalizedBaseDomain}/dashboard";
    }

    private async Task WriteTenantSwitchAuditAsync(
        Guid userId,
        Guid sourceTenantId,
        Guid targetTenantId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var detail = JsonSerializer.Serialize(new
        {
            sourceTenantId,
            targetTenantId,
        });

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = sourceTenantId,
            UserId = userId,
            EventType = "tenant_switch",
            Detail = detail,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
        });

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = targetTenantId,
            UserId = userId,
            EventType = "tenant_switch",
            Detail = detail,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// ISSUE-055 (US-AUTH-008): writes a security-audit row for a DENIED tenant switch (non-member,
    /// unavailable target, MFA-required, etc.). Actor is the requesting user; the attempted target tenant and
    /// the denial reason are recorded in the detail. Scoped to the source tenant the user is authenticated in.
    /// </summary>
    private async Task WriteTenantSwitchDeniedAuditAsync(
        Guid userId,
        Guid sourceTenantId,
        Guid targetTenantId,
        string reason,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAuditLogWithDetailAsync(
            userId,
            "tenant_switch_denied",
            ipAddress,
            userAgent,
            new { sourceTenantId, targetTenantId, reason },
            cancellationToken,
            sourceTenantId);
    }

    /// <summary>
    /// Writes an audit log entry for security-relevant events.
    /// </summary>
    private async Task WriteAuditLogAsync(
        Guid? userId,
        string eventType,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken,
        Guid? explicitTenantId = null)
    {
        var tenantId = explicitTenantId ?? (_tenantContext.IsResolved ? _tenantContext.TenantId : null);

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            UserId = userId,
            EventType = eventType,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Writes an audit log entry with structured detail JSON (US-AUTH-010 audit events).
    /// </summary>
    private async Task WriteAuditLogWithDetailAsync(
        Guid? userId,
        string eventType,
        string? ipAddress,
        string? userAgent,
        object detail,
        CancellationToken cancellationToken,
        Guid? explicitTenantId = null)
    {
        var tenantId = explicitTenantId ?? (_tenantContext.IsResolved ? _tenantContext.TenantId : null);

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            UserId = userId,
            EventType = eventType,
            Detail = JsonSerializer.Serialize(detail),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Deferred lockout-notification email params (BUG-045): captured inside the transactional unit
    /// but only enqueued to Hangfire AFTER a successful commit, so a rolled-back or retried attempt never
    /// sends a spurious lockout email.</summary>
    private sealed record LockoutNotification(string Email, string DisplayName, DateTime LockedUntil, int Minutes);

    /// <summary>
    /// BUG-045 / BUG-068: runs a failed-authentication-attempt handler as a single atomic, retriable unit.
    /// On a relational provider (Postgres/Npgsql) the delegate executes inside the Npgsql execution strategy
    /// — mandatory because <c>EnableRetryOnFailure</c> forbids a user-initiated <c>BeginTransactionAsync</c>
    /// outside the strategy — and within a transaction that first takes a pessimistic
    /// <c>SELECT ... FOR UPDATE</c> lock on the user row and reloads the tracked entity. Concurrent failed
    /// attempts for the same user therefore serialize on the row lock and each reads the AUTHORITATIVE
    /// counter (committed under the lock) before incrementing, closing the classic lost-update that let a
    /// parallelized brute-force never trip the lockout threshold. On a transient fault the whole unit is
    /// retried together; because the reload happens first, a retry re-reads fresh DB state and the counter
    /// increment stays idempotent (rather than reusing the rolled-back attempt's value).
    ///
    /// The delegate MUST call <c>SaveChangesAsync</c>; the commit is handled here. Non-transactional side
    /// effects (the Hangfire lockout email) MUST be deferred by the caller until AFTER this returns.
    ///
    /// On a non-relational provider (the EF InMemory provider used by some tests, which supports neither
    /// transactions nor raw SQL) the delegate simply runs directly — the previous single-threaded behavior.
    /// </summary>
    private async Task RunFailedAttemptAsync(User user, Func<Task> handleAsync, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            await handleAsync();
            return;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            // Pessimistic row lock: concurrent failed-attempt handlers for this user block here until this
            // unit commits, so each one reads a committed (not stale) counter before incrementing.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM users WHERE id = {user.Id} FOR UPDATE", cancellationToken);

            // Refresh the tracked entity with the authoritative counters committed under the lock (and, on a
            // transient retry, re-read fresh DB state instead of the rolled-back attempt's in-memory values).
            await _dbContext.Entry(user).ReloadAsync(cancellationToken);

            await handleAsync();

            await transaction.CommitAsync(cancellationToken);
        });
    }

    /// <summary>
    /// Resolves the lockout policy from the current tenant context, falling back to defaults.
    /// Returns (maxAttempts, lockoutDurationMinutes, progressiveLockoutEnabled).
    /// </summary>
    private async Task<(int maxAttempts, int lockoutMinutes, bool progressiveLockoutEnabled)> GetLockoutPolicyAsync(
        CancellationToken cancellationToken)
    {
        var maxAttempts = 5;
        var lockoutMinutes = 15;
        var progressiveLockoutEnabled = false;

        if (_tenantContext.IsResolved)
        {
            var tenant = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, cancellationToken);
            if (tenant is not null)
            {
                maxAttempts = tenant.MaxFailedAttempts > 0 ? tenant.MaxFailedAttempts : 5;
                lockoutMinutes = tenant.LockoutDurationMinutes > 0 ? tenant.LockoutDurationMinutes : 15;
                progressiveLockoutEnabled = tenant.ProgressiveLockoutEnabled;
            }
        }

        return (maxAttempts, lockoutMinutes, progressiveLockoutEnabled);
    }

    /// <summary>
    /// FR-9: Calculate effective lockout duration with progressive doubling.
    /// If progressive lockout is enabled and the user has had multiple lockout cycles
    /// within the last 24 hours, the duration doubles for each recent cycle.
    /// </summary>
    private static int CalculateProgressiveLockoutMinutes(
        User user,
        int baseLockoutMinutes,
        bool progressiveLockoutEnabled)
    {
        if (!progressiveLockoutEnabled)
            return baseLockoutMinutes;

        // Count recent lockouts within the 24-hour window
        var recentLockoutCount = 0;
        if (user.LastLockoutAt.HasValue &&
            (DateTime.UtcNow - user.LastLockoutAt.Value).TotalHours < 24)
        {
            recentLockoutCount = user.LockoutCount;
        }

        // Double duration for each recent lockout cycle, capped at 8x (3 doublings)
        var multiplier = 1;
        for (var i = 0; i < Math.Min(recentLockoutCount, 3); i++)
        {
            multiplier *= 2;
        }

        return baseLockoutMinutes * multiplier;
    }

    #endregion
}
