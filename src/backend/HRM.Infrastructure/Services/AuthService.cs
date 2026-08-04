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
    // BUG-294: accepting an invitation grants roles, so any permissions cached for this user+tenant are stale
    // the moment the membership goes Active. Optional (nullable, default null) like the deps above so isolated
    // unit construction still compiles; DI injects the registered cache in production.
    private readonly IPermissionCache? _permissionCache;

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
        IMyTenantsCache? myTenantsCache = null,
        IPermissionCache? permissionCache = null)
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
        _permissionCache = permissionCache;
    }

    public Task<Result<LoginResponse>> LoginAsync(
        string email,
        string password,
        string? mfaCode,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
        => LoginInternalAsync(email, password, mfaCode, ipAddress, userAgent, breakGlass: false, cancellationToken);

    /// <inheritdoc />
    public Task<Result<LoginResponse>> BreakGlassLoginAsync(
        string email,
        string password,
        string? mfaCode,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
        => LoginInternalAsync(email, password, mfaCode, ipAddress, userAgent, breakGlass: true, cancellationToken);

    /// <summary>
    /// Shared local-credential login core for both the standard (<paramref name="breakGlass"/> = false) and the
    /// US-AUTH-016 break-glass (true) paths. The two differ only in SSO-enforcement handling (see
    /// <see cref="EvaluateSsoEnforcementAsync"/>): the standard path is REFUSED under <c>sso_only</c>; the
    /// break-glass path is permitted ONLY for a designated break-glass admin and, on success, emits the
    /// high-severity <c>break_glass_login</c> audit + admin alert.
    /// </summary>
    private async Task<Result<LoginResponse>> LoginInternalAsync(
        string email,
        string password,
        string? mfaCode,
        string? ipAddress,
        string? userAgent,
        bool breakGlass,
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
                // ISSUE-063: enrich with the login-time tenant name (the tenant is not yet loaded on this path).
                lockoutEmail = lockoutEmail with { TenantName = await ResolveTenantNameAsync(cancellationToken) };
                // DF-40: capture the tenant id as a local so Hangfire serializes a value, not a service closure.
                var lockoutTenantId = _tenantContext.TenantId;
                _backgroundJobClient.Enqueue<ILockoutNotificationService>(
                    svc => svc.SendLockoutNotificationAsync(lockoutEmail.Email, lockoutEmail.DisplayName, lockoutEmail.LockedUntil, lockoutEmail.Minutes, lockoutEmail.TenantName, lockoutTenantId, default));
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

        // 5a. US-AUTH-016 (FR-1/AC-1/AC-2/AC-7): SSO enforcement decision. On the STANDARD path this refuses local
        // logins under sso_only; on the break-glass path it permits ONLY a designated break-glass admin (BR-2).
        // Evaluated from the cached SSO snapshot (NFR-4) — no Entra/allow-list dependency, so break-glass keeps
        // working even when SSO is misconfigured/unreachable (NFR-1).
        var enforcement = await EvaluateSsoEnforcementAsync(user, currentTenant.Id, breakGlass, ipAddress, userAgent, cancellationToken);
        if (enforcement is not null)
        {
            return enforcement;
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
                    // ISSUE-063: reuse the already-loaded login-time tenant for the email branding (no extra query).
                    lockoutEmail = lockoutEmail with { TenantName = currentTenant.Name };
                    // DF-40: reuse the already-loaded tenant id (a value, so Hangfire serializes it cleanly).
                    var lockoutTenantId = currentTenant.Id;
                    _backgroundJobClient.Enqueue<ILockoutNotificationService>(
                        svc => svc.SendLockoutNotificationAsync(lockoutEmail.Email, lockoutEmail.DisplayName, lockoutEmail.LockedUntil, lockoutEmail.Minutes, lockoutEmail.TenantName, lockoutTenantId, default));
                }

                return Result<LoginResponse>.Failure("Invalid verification code.", 401);
            }
        }

        // 7. Issue tokens via shared helper
        var tokenResult = await IssueTokensAsync(user, currentTenant, userTenant, ipAddress, userAgent, cancellationToken);

        // US-AUTH-016 FR-4/BR-4 (NFR-2): a completed break-glass login is a high-severity security event —
        // audit it + alert admins. Emitted only on actual token issuance (an MFA-challenge return above exits
        // before here), so we never alert on an incomplete login. The designation was already enforced at 5a.
        if (breakGlass && tokenResult.IsSuccess)
        {
            await EmitBreakGlassLoginAsync(user, currentTenant, ipAddress, userAgent, cancellationToken);
        }

        return tokenResult;
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
                // ISSUE-059: record the session metadata (revoked session, idle duration) in the audit detail.
                await WriteAuditLogWithDetailAsync(
                    storedToken.UserId, "session_expired_idle", ipAddress, userAgent,
                    new
                    {
                        revokedSessionId = storedToken.Id,
                        idleDurationMinutes = Math.Round(idleMinutes, 2),
                        idleTimeoutMinutes = tenant.IdleTimeoutMinutes,
                    },
                    cancellationToken, storedToken.TenantId);
                return Result<RefreshTokenResponse>.Failure("Session expired due to inactivity.", 401);
            }
        }

        // Check absolute timeout (US-AUTH-009 AC-3)
        var absoluteHours = (DateTime.UtcNow - storedToken.IssuedAt).TotalHours;
        if (absoluteHours > tenant.AbsoluteTimeoutHours)
        {
            storedToken.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            // ISSUE-059: record the session metadata (revoked session, absolute duration) in the audit detail.
            await WriteAuditLogWithDetailAsync(
                storedToken.UserId, "session_expired_absolute", ipAddress, userAgent,
                new
                {
                    revokedSessionId = storedToken.Id,
                    sessionDurationHours = Math.Round(absoluteHours, 2),
                    absoluteTimeoutHours = tenant.AbsoluteTimeoutHours,
                },
                cancellationToken, storedToken.TenantId);
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
            // ISSUE-059: record which session was self-revoked in the audit detail.
            await WriteAuditLogWithDetailAsync(
                storedToken.UserId, "logout", null, null,
                new { revokedSessionId = storedToken.Id },
                cancellationToken, storedToken.TenantId);

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
            // BUG-295: the Angular route is NESTED under /auth. This link previously pointed at the ROOT
            // /reset-password, which no route matches, so the SPA wildcard redirected every recipient to the
            // login page and discarded the token — self-service password reset was dead end-to-end. The path
            // here and the route table must agree; AuthEmailLinkRouteTests pins that they do.
            var resetUrl = $"https://{subdomain}.{baseDomain}/auth/reset-password?token={rawToken}";
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

    /// <summary>
    /// Resets a password from an emailed reset link. The TOKEN ALONE identifies the user (BUG-295).
    ///
    /// <para>This used to also require the email address, which is why the feature was dead end-to-end: the
    /// emailed link carried only <c>?token=</c>, so the page could never assemble a valid request. The email
    /// added no security — the reset token is 256 bits of entropy stored against exactly one <c>User</c> row, so
    /// whoever holds it is already identified — while adding a second thing that had to agree, and putting a PII
    /// value into a URL that lands in proxy logs, browser history and referrer headers. Requiring one secret
    /// instead of one secret plus one identifier is both simpler and strictly better.</para>
    ///
    /// <para>Looking the user up BY the token hash mirrors how refresh tokens are already resolved in this
    /// class. The stored hash is itself derived from the secret, so an equality lookup reveals nothing to an
    /// attacker who does not already hold the token.</para>
    /// </summary>
    public async Task<Result> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        // Every rejection below returns the SAME message — a distinct "no such token" vs "expired" would be an
        // oracle. Preserved verbatim from the pre-BUG-295 behaviour.
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure("The reset link is invalid or has expired. Please request a new one.", 400);
        }

        var tokenHash = HashResetToken(token);

        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.PasswordResetTokenHash == tokenHash, cancellationToken);

        if (user is null ||
            user.PasswordResetTokenExpiresAt is null ||
            user.PasswordResetTokenExpiresAt.Value <= DateTime.UtcNow)
        {
            return Result.Failure("The reset link is invalid or has expired. Please request a new one.", 400);
        }

        // Single-use: consume the token so it cannot be replayed. Mutated in-memory only here — it is persisted
        // together with the new hash by the shared apply helper's SaveChanges, and ONLY on success. If policy /
        // history validation inside the helper fails, nothing is saved, so the stored token stays valid and the
        // user can retry with the same link (BUG-004 / ISSUE-053 behavior preserved).
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;

        return await ChangeUserPasswordAsync(user, newPassword, "password_reset_completed", null, null, cancellationToken);
    }

    /// <summary>
    /// BUG-294: redeems a tenant user-invitation — the missing other half of US-ADM-005.
    ///
    /// <para>The invite side was complete (token minted, stored, rotated on resend, plan-limit enforced,
    /// audited) but <b>nothing ever verified the token and no endpoint accepted it</b>, so every invitation
    /// email carried a live secret to a route the backend could not honour and
    /// <see cref="InvitationStatus.Accepted"/> was unreachable code. This is that missing half.</para>
    ///
    /// <para><b>It deliberately does not build a second password rail.</b> The invitee is created passwordless
    /// at invite time, so setting the first password is routed through the very same
    /// <c>ChangeUserPasswordAsync</c> the reset flow uses — which means the tenant password policy, the
    /// re-use history rules and the refresh-token revocation all apply here for free and cannot drift apart.
    /// That helper also owns the single <c>SaveChanges</c>, so the membership, the role grants, the status flip
    /// and the new password commit as ONE unit: a policy rejection persists nothing and leaves the link
    /// usable for a retry.</para>
    ///
    /// <para>Lives in <c>AuthService</c> rather than <c>UserManagementService</c> because the caller is
    /// ANONYMOUS — the invitee has no session yet, whereas every path in the user-management service assumes an
    /// authenticated admin.</para>
    /// </summary>
    public async Task<Result> AcceptInvitationAsync(
        string token,
        string newPassword,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        // The invitation is tenant-scoped, and the link lands on the tenant's own subdomain, so an unresolved
        // tenant means the request never reached its workspace — fail rather than search every tenant.
        if (!_tenantContext.IsResolved)
        {
            return Result.Failure("Workspace could not be determined for this invitation link.", 400);
        }

        const string GenericFailure =
            "This invitation link is invalid, has expired, or has already been used. Please ask your administrator to send a new one.";

        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure(GenericFailure, 400);
        }

        var tokenHash = InvitationToken.Hash(token);

        // The global query filter scopes this to the resolved tenant, so a tenant-A token presented on
        // tenant B's subdomain simply does not resolve — cross-tenant redemption is impossible by construction.
        var invitation = await _dbContext.UserInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

        // One message for "no such token", "revoked", "already accepted" and "expired" alike: distinguishing
        // them would tell an attacker which guesses were once real invitations.
        if (invitation is null ||
            invitation.Status != InvitationStatus.Invited ||
            invitation.ExpiresAt <= DateTime.UtcNow)
        {
            return Result.Failure(GenericFailure, 400);
        }

        // Belt-and-braces constant-time confirmation. The lookup above already matched on equality; this makes
        // the comparison explicit and keeps the verification in one place should the lookup ever change.
        if (!InvitationToken.Verify(token, invitation.TokenHash))
        {
            return Result.Failure(GenericFailure, 400);
        }

        var email = invitation.Email.Trim().ToLowerInvariant();

        // Users are global (no tenant filter). The invite path find-or-creates this row, so its absence means
        // the invitation outlived its user — treat as unredeemable rather than silently creating an account.
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning(
                "Invitation {InvitationId} in tenant {TenantId} has no matching user row for its email; " +
                "cannot be redeemed.",
                invitation.Id, _tenantContext.TenantId);
            return Result.Failure(GenericFailure, 400);
        }

        var now = DateTime.UtcNow;

        // Membership: create it, or re-activate a disabled one. An invitation is only issued when no ACTIVE
        // membership exists, so re-activating here is the intended outcome of accepting.
        // Deliberately NOT IgnoreQueryFilters: UserTenant IS tenant-scoped by a global filter, so bypassing it
        // here would leave the explicit TenantId predicate below as the only thing preventing a cross-tenant
        // membership read — and the next person to see a "redundant" predicate would delete it. Two independent
        // guards, neither load-bearing alone.
        var membership = await _dbContext.UserTenants
            .FirstOrDefaultAsync(
                ut => ut.UserId == user.Id && ut.TenantId == _tenantContext.TenantId, cancellationToken);

        if (membership is null)
        {
            membership = new UserTenant
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = user.Id,
                TenantId = _tenantContext.TenantId,
                Status = UserTenantStatus.Active,
                CreatedAt = now,
            };
            _dbContext.UserTenants.Add(membership);
        }
        else
        {
            membership.Status = UserTenantStatus.Active;
            membership.UpdatedAt = now;
        }

        // Grant the invited roles — but only those that STILL EXIST in this tenant. A role can be deleted
        // during the 72-hour window, and granting a dangling id would blow up on the FK and make a legitimate
        // invitation permanently unredeemable. Dropping the vanished role is the graceful outcome: the user
        // gets in, an admin can adjust their roles afterwards.
        var invitedRoleIds = invitation.InvitedRoleIds ?? new List<Guid>();
        var liveRoleIds = invitedRoleIds.Count == 0
            ? new List<Guid>()
            : await _dbContext.Roles
                .Where(r => invitedRoleIds.Contains(r.Id))
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

        if (liveRoleIds.Count != invitedRoleIds.Count)
        {
            _logger.LogWarning(
                "Invitation {InvitationId} referenced {Missing} role(s) that no longer exist in tenant " +
                "{TenantId}; they were skipped on acceptance.",
                invitation.Id, invitedRoleIds.Count - liveRoleIds.Count, _tenantContext.TenantId);
        }

        var existingRoleIds = await _dbContext.UserTenantRoles
            .IgnoreQueryFilters()
            .Where(x => x.UserTenantId == membership.Id)
            .Select(x => x.RoleId)
            .ToListAsync(cancellationToken);

        foreach (var roleId in liveRoleIds.Where(id => !existingRoleIds.Contains(id)))
        {
            _dbContext.UserTenantRoles.Add(new UserTenantRole
            {
                UserTenantId = membership.Id,
                RoleId = roleId,
                AssignedAt = now,
                AssignedBy = "invitation",
            });
        }

        // Consume the invitation. In-memory only — the shared password helper's SaveChanges commits this
        // together with everything above, so a password-policy rejection leaves the invitation Invited and the
        // link still usable (the same idiom the reset path uses).
        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedAt = now;

        var result = await ChangeUserPasswordAsync(
            user, newPassword, "invitation_accepted", ipAddress, userAgent, cancellationToken);

        if (result.IsFailure)
        {
            return result;
        }

        // The membership is Active only as of this commit, so anything cached about this user is now stale.
        // my-tenants is the load-bearing one: a user invited into a SECOND workspace may hold a cached list
        // that omits it for up to the 5-minute TTL (BUG-116 class).
        if (_permissionCache is not null)
        {
            await _permissionCache.InvalidateAsync(_tenantContext.TenantId, user.Id, cancellationToken);
        }

        if (_myTenantsCache is not null)
        {
            await _myTenantsCache.InvalidateAsync(user.Id, cancellationToken);
        }

        _logger.LogInformation(
            "Invitation accepted. InvitationId={InvitationId}, UserId={UserId}, TenantId={TenantId}, " +
            "RolesGranted={RoleCount}",
            invitation.Id, user.Id, _tenantContext.TenantId, liveRoleIds.Count);

        return Result.Success();
    }

    /// <summary>
    /// US-AUTH-004 (ISSUE-248): authenticated self-service change-password. The caller must have proven identity
    /// (a valid JWT) and supply the CURRENT password; both the tenant password policy and the history rules (FR-5)
    /// are enforced via the same shared apply path as the token-based reset, so history can never be bypassed here.
    /// </summary>
    public async Task<Result> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure("User not found.", 404);
        }

        // Verify the current password before allowing a change (generic message — no oracle on which field failed).
        if (string.IsNullOrEmpty(user.PasswordHash)
            || !BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
        {
            await WriteAuditLogAsync(user.Id, "password_change_failed", ipAddress, userAgent, cancellationToken);
            return Result.Failure("Your current password is incorrect.", 400, "invalid_current_password");
        }

        return await ChangeUserPasswordAsync(user, newPassword, "password_changed", ipAddress, userAgent, cancellationToken);
    }

    /// <summary>
    /// US-AUTH-004 FR-5 (ISSUE-053 / ISSUE-248): applies a NEW password to an ALREADY-AUTHORIZED user (the caller has
    /// proven identity — a valid reset token, or the current password). Enforces the tenant password policy + history
    /// (reject reuse of the last N), sets the new hash, clears lockout state (BR-2), revokes all refresh tokens across
    /// tenants, records + prunes history, and audits <paramref name="auditEvent"/>. On a policy/history failure it
    /// returns WITHOUT saving, so any in-memory mutation the caller made first (e.g. a consumed reset token) is never
    /// persisted and the user can retry. Shared by the reset and self-service change paths so FR-5 can't be bypassed.
    /// </summary>
    private async Task<Result> ChangeUserPasswordAsync(
        User user,
        string newPassword,
        string auditEvent,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        // BUG-004: enforce the TENANT's configured password policy (min length, complexity), not just the
        // hardcoded validator defaults. Validated BEFORE anything is saved so a policy failure is retriable.
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
        // Runs after policy validation and BEFORE any save, so a rejection is retriable. historyCount <= 0 skips it.
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

        // Hash and set new password (BR-2: a password change/reset clears lockout state).
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

        // ISSUE-051 (US-AUTH-004 FR-8): audit the successful password change/reset. Reached only after the caller's
        // identity is validated (reset token or current password), so the user id is a real, authorized subject.
        await WriteAuditLogAsync(user.Id, auditEvent, ipAddress, userAgent, cancellationToken);

        _logger.LogInformation("Password updated for user {UserId} ({AuditEvent})", user.Id, auditEvent);

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
            // ISSUE-059: record which sessions were revoked (count + ids) in the audit detail.
            await WriteAuditLogWithDetailAsync(
                userId, "session_revoked_by_admin", null, null,
                new
                {
                    revokedSessionCount = tokens.Count,
                    revokedSessionIds = tokens.Select(t => t.Id).ToList(),
                },
                cancellationToken, tenantId);
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

        // ISSUE-057 (US-AUTH-008): resolve membership BEFORE inspecting the target tenant's lifecycle status,
        // so a NON-member can never learn that tenant's state. A non-member gets the same generic membership
        // error regardless of whether the target is active, suspended, or terminating.
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

        if (targetTenant.Status is not (TenantStatus.Active or TenantStatus.Trial))
        {
            // ISSUE-055 (US-AUTH-008): audit the denial (target tenant not in an accessible state). The audit
            // reason retains the precise status for forensics; ISSUE-057: the CALLER-facing message stays
            // generic and never discloses the exact TenantStatus enum value.
            await WriteTenantSwitchDeniedAuditAsync(
                userId, sourceTenantId, targetTenantId, $"target_tenant_{targetTenant.Status}", ipAddress, userAgent, cancellationToken);
            return Result<SwitchTenantResponse>.Failure(
                "The target organization is currently unavailable.",
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
        // ISSUE-241 (US-AUTH-005 AC-7): remember when THIS login burned a recovery code so the success response can
        // report the remaining count and prompt the user to regenerate their codes once they run low.
        var usedRecoveryCode = false;

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
                        usedRecoveryCode = true;
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
                // ISSUE-063: enrich with the login-time tenant name (the tenant is loaded later on this path).
                lockoutEmail = lockoutEmail with { TenantName = await ResolveTenantNameAsync(cancellationToken) };
                // DF-40: capture the tenant id as a local so Hangfire serializes a value, not a service closure.
                var lockoutTenantId = _tenantContext.TenantId;
                _backgroundJobClient.Enqueue<ILockoutNotificationService>(
                    svc => svc.SendLockoutNotificationAsync(lockoutEmail.Email, lockoutEmail.DisplayName, lockoutEmail.LockedUntil, lockoutEmail.Minutes, lockoutEmail.TenantName, lockoutTenantId, default));
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

        // ISSUE-241: if this login consumed a recovery code, count the codes still unused (the just-used one is
        // already persisted with UsedAt set above) so the response can nudge the user to regenerate when low.
        int? recoveryCodesRemaining = usedRecoveryCode
            ? await _dbContext.MfaRecoveryCodes
                .IgnoreQueryFilters()
                .CountAsync(rc => rc.UserId == user.Id && rc.UsedAt == null, cancellationToken)
            : null;

        var tokenResult = await IssueTokensAsync(
            user, currentTenant, userTenant, ipAddress, userAgent, cancellationToken, recoveryCodesRemaining);

        // ISSUE-327 (US-AUTH-016 FR-4/NFR-2): a designated break-glass admin who has MFA enrolled completes login
        // through THIS two-step MFA verify, not the single-shot LoginInternalAsync path — so the high-severity
        // break_glass_login audit + admin alert must fire here too, identically to the single-shot path. The
        // break-glass "marker" set at step 1 is re-derived from the same cached SSO snapshot seam rather than
        // threaded as fragile cross-request state: under sso_only the ONLY way to reach a completed MFA verify is
        // via the break-glass step-1 (EvaluateSsoEnforcementAsync refuses every standard local login under
        // sso_only, designated or not), so "sso_only + designated admin" is exactly the break-glass condition.
        // Emitted only on actual token issuance; the designation gate is preserved, so an ordinary (non-designated)
        // MFA user never triggers break-glass telemetry. Reuses EmitBreakGlassLoginAsync (no duplicated audit/notify).
        if (tokenResult.IsSuccess && await IsBreakGlassMfaLoginAsync(user.Id, currentTenant.Id, cancellationToken))
        {
            await EmitBreakGlassLoginAsync(user, currentTenant, ipAddress, userAgent, cancellationToken);
        }

        return tokenResult;
    }

    /// <summary>
    /// ISSUE-327 (US-AUTH-016 FR-4/BR-2): true when a completed two-step MFA verify is actually a break-glass login —
    /// enforcement is <c>sso_only</c> AND the user is a designated break-glass admin. Under <c>sso_only</c> the
    /// two-step MFA challenge can only have been issued by the break-glass step-1 (the standard path is refused for
    /// everyone), so this re-derives the same designation gate from the cached SSO snapshot (NFR-4) instead of
    /// threading cross-request state that a cache blip could silently lose (under-alerting a security event).
    /// </summary>
    private async Task<bool> IsBreakGlassMfaLoginAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var sso = (await GetSsoSettingsAsync(tenantId, cancellationToken)).Value ?? new SsoSettingsSnapshot();
        return sso.EnforcementMode == SsoEnforcementModes.SsoOnly
            && sso.BreakGlassAdminUserIds.Contains(userId.ToString());
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

        // US-AUTH-012 FR-8/NFR-1: SSO subset is served through the cache-aside snapshot method (the same seam the
        // login/callback path uses); entitlement is resolved from the tenant's plan for the FE's enable/disable UI.
        var sso = (await GetSsoSettingsAsync(tenantId, cancellationToken)).Value ?? new SsoSettingsSnapshot();
        var ssoEntitled = await IsSsoEntitledAsync(tenant.PlanId, cancellationToken);

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
            // US-AUTH-012 FR-1/FR-2: SSO settings
            SsoEnabled = sso.SsoEnabled,
            AllowedEntraTenantIds = sso.AllowedEntraTenantIds,
            AllowedEmailDomains = sso.AllowedEmailDomains,
            JitEnabled = sso.JitEnabled,
            JitDefaultRole = sso.JitDefaultRole,
            EnforcementMode = sso.EnforcementMode,
            SsoEntitled = ssoEntitled,
            // US-AUTH-016: enforcement designations + onboarding progress for the Security > SSO UI.
            BreakGlassAdminUserIds = sso.BreakGlassAdminUserIds,
            SsoOnboardingStatus = tenant.SsoOnboardingStatus,
        });
    }

    /// <inheritdoc />
    public async Task<Result<SsoSettingsSnapshot>> GetSsoSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = SsoSettingsCacheKey(tenantId);

        // Cache read is best-effort — a Redis outage falls back to the DB (mirrors the my-tenants cache).
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                var snap = JsonSerializer.Deserialize<SsoSettingsSnapshot>(cached);
                if (snap is not null)
                {
                    return Result<SsoSettingsSnapshot>.Success(snap);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSO settings cache read failed for tenant {TenantId}; falling back to database", tenantId);
        }

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            return Result<SsoSettingsSnapshot>.Failure("Tenant not found.", 404);
        }

        var snapshot = ToSsoSnapshot(tenant);

        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(snapshot),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSO settings cache write failed for tenant {TenantId}", tenantId);
        }

        return Result<SsoSettingsSnapshot>.Success(snapshot);
    }

    private static string SsoSettingsCacheKey(Guid tenantId) => $"sso-settings:{tenantId}";

    private static SsoSettingsSnapshot ToSsoSnapshot(Tenant tenant) => new()
    {
        SsoEnabled = tenant.SsoEnabled,
        AllowedEntraTenantIds = tenant.AllowedEntraTenantIds ?? [],
        AllowedEmailDomains = tenant.AllowedEmailDomains ?? [],
        JitEnabled = tenant.JitEnabled,
        JitDefaultRole = tenant.JitDefaultRole,
        EnforcementMode = tenant.SsoEnforcementMode,
        BreakGlassAdminUserIds = tenant.BreakGlassAdminUserIds ?? [],
    };

    /// <summary>
    /// US-AUTH-016 FR-1/AC-1/AC-2/AC-7: the SSO-enforcement gate for local logins. Returns a non-null failure to
    /// REFUSE the login; null to proceed. Reads the cached SSO snapshot (NFR-4) — no Entra/allow-list dependency,
    /// so the break-glass path (NFR-1) never breaks when SSO is misconfigured or unreachable.
    ///
    /// <para>Break-glass path: permitted ONLY when the user is a designated break-glass admin (BR-2); an
    /// ordinary user is refused (AC-7) and audited (<c>break_glass_login_denied</c>). Standard path: refused
    /// under <c>sso_only</c> (AC-1) with the "requires Microsoft" message, regardless of designation (a
    /// break-glass admin must use the break-glass path). Under <c>optional</c> the standard path proceeds.</para>
    /// </summary>
    private async Task<Result<LoginResponse>?> EvaluateSsoEnforcementAsync(
        User user, Guid tenantId, bool breakGlass, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        var sso = (await GetSsoSettingsAsync(tenantId, cancellationToken)).Value ?? new SsoSettingsSnapshot();
        var isSsoOnly = sso.EnforcementMode == SsoEnforcementModes.SsoOnly;
        var isDesignated = sso.BreakGlassAdminUserIds.Contains(user.Id.ToString());

        if (breakGlass)
        {
            // BR-2/AC-7: break-glass is restricted to explicitly designated admin accounts.
            if (!isDesignated)
            {
                await WriteAuditLogAsync(user.Id, "break_glass_login_denied", ipAddress, userAgent, cancellationToken, tenantId);
                return Result<LoginResponse>.Failure(
                    "This sign-in method is restricted to designated administrators. Please contact your administrator.", 403);
            }

            // Designated → proceed (the break_glass_login audit + alert fire on successful token issuance).
            return null;
        }

        // Standard path: under sso_only, refuse ALL local logins (AC-1) — the SSO path or the break-glass path
        // are the only accepted routes. A designated admin who lands here is told to use Microsoft / break-glass.
        if (isSsoOnly)
        {
            return Result<LoginResponse>.Failure(
                "Your organization requires sign-in with Microsoft. Please use the 'Sign in with Microsoft' option, or contact your administrator.", 403);
        }

        return null;
    }

    /// <summary>
    /// US-AUTH-016 FR-4/BR-4 (NFR-2): records the high-severity <c>break_glass_login</c> audit event (secrets
    /// excluded) and enqueues the admin alert on Hangfire (delivered within 60s, off the login path). Primitives
    /// only into the job so Hangfire serializes a value payload, not a service closure.
    /// </summary>
    private async Task EmitBreakGlassLoginAsync(User user, Tenant tenant, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        var occurredAt = DateTime.UtcNow;

        await WriteAuditLogWithDetailAsync(
            user.Id, "break_glass_login", ipAddress, userAgent,
            new
            {
                severity = "high",
                tenantId = tenant.Id,
                userEmail = user.Email,
                sourceIp = ipAddress,
                occurredAtUtc = occurredAt,
            },
            cancellationToken, tenant.Id);

        var tenantId = tenant.Id;
        var tenantName = tenant.Name;
        var userId = user.Id;
        var userEmail = user.Email;
        var displayName = user.DisplayName;
        _backgroundJobClient.Enqueue<IBreakGlassNotificationService>(
            svc => svc.SendBreakGlassAlertAsync(tenantId, tenantName, userId, userEmail, displayName, ipAddress, occurredAt, default));

        _logger.LogWarning("Break-glass login by admin {UserId} in tenant {TenantId} from {SourceIp}.",
            user.Id, tenant.Id, ipAddress ?? "unknown");
    }

    /// <summary>
    /// US-AUTH-016 FR-2/FR-3 (BR-2): from a set of candidate user ids, returns those that are VALID break-glass
    /// admins for the tenant — an active membership whose user is active AND has a password (local-login capable)
    /// AND holds a Tenant Owner/Admin role. This is the anti-lockout guarantee: only real local admins count.
    /// </summary>
    private async Task<HashSet<string>> GetValidBreakGlassAdminIdsAsync(
        Guid tenantId, IReadOnlyCollection<string> candidateIds, CancellationToken cancellationToken)
    {
        if (candidateIds.Count == 0)
        {
            return new HashSet<string>();
        }

        var guids = candidateIds
            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();

        if (guids.Count == 0)
        {
            return new HashSet<string>();
        }

        var adminRoles = new[]
        {
            PermissionCatalog.BuiltInRoles.TenantOwner,
            PermissionCatalog.BuiltInRoles.TenantAdmin,
        };

        var valid = await _dbContext.UserTenants
            .IgnoreQueryFilters()
            .Where(ut => ut.TenantId == tenantId
                && guids.Contains(ut.UserId)
                && ut.Status == UserTenantStatus.Active
                && ut.User.IsActive
                && ut.User.PasswordHash != null
                && ut.UserTenantRoles.Any(utr => adminRoles.Contains(utr.Role.Name)))
            .Select(ut => ut.UserId)
            .ToListAsync(cancellationToken);

        return valid.Select(id => id.ToString()).ToHashSet();
    }

    /// <summary>US-AUTH-012 FR-3: resolves the SSO entitlement from the tenant's subscription plan (US-ADM-009).</summary>
    private async Task<bool> IsSsoEntitledAsync(string planId, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == planId, cancellationToken);

        return plan?.FeatureFlags.Sso ?? false;
    }

    private async Task InvalidateSsoSettingsCacheAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RemoveAsync(SsoSettingsCacheKey(tenantId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSO settings cache invalidation failed for tenant {TenantId}", tenantId);
        }
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

        // ── US-AUTH-012: SSO settings write path (validated before any mutation so a failure changes nothing) ──
        var ssoWrite = request.SsoEnabled.HasValue
            || request.AllowedEntraTenantIds is not null
            || request.AllowedEmailDomains is not null
            || request.JitEnabled.HasValue
            || request.JitDefaultRole is not null
            || request.EnforcementMode is not null
            || request.BreakGlassAdminUserIds is not null;

        object? ssoBefore = null;
        var ssoChanged = false;
        var enforcementModeChanged = false;
        string? previousEnforcementMode = null;

        if (ssoWrite)
        {
            // FR-3/AC-2: the entire SSO surface is gated on the plan's SSO entitlement.
            if (!await IsSsoEntitledAsync(tenant.PlanId, cancellationToken))
            {
                return Result.Failure("SSO is not included in your current plan.", 403, errorCode: "sso_not_entitled");
            }

            // Merged effective state — a null request field means "leave unchanged"; a provided list REPLACES.
            var effEnabled = request.SsoEnabled ?? tenant.SsoEnabled;
            var effTids = (request.AllowedEntraTenantIds ?? tenant.AllowedEntraTenantIds ?? [])
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var effDomains = (request.AllowedEmailDomains ?? tenant.AllowedEmailDomains ?? [])
                .Select(d => d.Trim().ToLowerInvariant())
                .Where(d => d.Length > 0)
                .Distinct()
                .ToList();
            var effJitEnabled = request.JitEnabled ?? tenant.JitEnabled;
            var effJitRole = request.JitDefaultRole is null
                ? tenant.JitDefaultRole
                : (string.IsNullOrWhiteSpace(request.JitDefaultRole) ? null : request.JitDefaultRole.Trim());
            var effMode = request.EnforcementMode ?? tenant.SsoEnforcementMode;
            // US-AUTH-016: a provided list REPLACES; null leaves the stored designations unchanged.
            var effBreakGlass = (request.BreakGlassAdminUserIds ?? tenant.BreakGlassAdminUserIds ?? [])
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // FR-5/BR-3 (fail-closed): SSO cannot be enabled with an empty allow-list. Authoritative merged-state
            // guard (the validator only catches the same-request case).
            if (effEnabled && effTids.Count == 0 && effDomains.Count == 0)
            {
                return Result.Failure("Add at least one trusted directory or email domain before enabling SSO.", 400);
            }

            // FR-6/BR-5: the JIT default role must exist in this tenant and must not be a privileged admin/owner role.
            if (!string.IsNullOrWhiteSpace(effJitRole))
            {
                if (PermissionCatalog.BuiltInRoles.PrivilegedForJit.Contains(effJitRole))
                {
                    return Result.Failure("The default SSO role cannot be a privileged admin or owner role.", 400);
                }

                var roleExists = await _dbContext.Roles
                    .IgnoreQueryFilters()
                    .AnyAsync(r => r.TenantId == tenantId && r.Name == effJitRole, cancellationToken);

                if (!roleExists)
                {
                    return Result.Failure($"Role '{effJitRole}' does not exist in this tenant.", 400);
                }
            }

            // US-AUTH-016 FR-2/BR-2: when a designation list is PROVIDED, every id must resolve to a valid local
            // (password) admin of this tenant — a bogus/non-admin designation would give a false anti-lockout
            // guarantee. (A null list — "leave unchanged" — is not re-validated here.)
            if (request.BreakGlassAdminUserIds is not null && effBreakGlass.Count > 0)
            {
                var validProvided = await GetValidBreakGlassAdminIdsAsync(tenantId, effBreakGlass, cancellationToken);
                if (effBreakGlass.Any(id => !validProvided.Contains(id)))
                {
                    return Result.Failure(
                        "Each break-glass admin must be an active local (password) administrator of this workspace.", 400);
                }
            }

            // US-AUTH-016 FR-3/AC-3/BR-1: sso_only is accepted only when at least one DESIGNATED break-glass admin
            // is a valid local admin — the mandatory anti-lockout path. Blocks the change with a clear explanation
            // otherwise. Replaces the US-AUTH-012 "any local admin exists" precondition with explicit designation.
            if (effMode == SsoEnforcementModes.SsoOnly)
            {
                var validBreakGlass = await GetValidBreakGlassAdminIdsAsync(tenantId, effBreakGlass, cancellationToken);
                if (validBreakGlass.Count == 0)
                {
                    return Result.Failure(
                        "Designate at least one local (password) admin as a break-glass path before enforcing SSO-only sign-in. " +
                        "We also recommend a successful SSO test login first.",
                        400);
                }
            }

            // FR-7: capture the before-state for the audit trail (no secret material is stored per tenant).
            ssoBefore = new
            {
                tenant.SsoEnabled,
                tenant.AllowedEntraTenantIds,
                tenant.AllowedEmailDomains,
                tenant.JitEnabled,
                tenant.JitDefaultRole,
                EnforcementMode = tenant.SsoEnforcementMode,
                tenant.BreakGlassAdminUserIds,
                tenant.SsoOnboardingStatus,
            };

            previousEnforcementMode = tenant.SsoEnforcementMode;
            enforcementModeChanged = effMode != tenant.SsoEnforcementMode;

            tenant.SsoEnabled = effEnabled;
            tenant.AllowedEntraTenantIds = effTids;
            tenant.AllowedEmailDomains = effDomains;
            tenant.JitEnabled = effJitEnabled;
            tenant.JitDefaultRole = effJitRole;
            tenant.SsoEnforcementMode = effMode;
            tenant.BreakGlassAdminUserIds = effBreakGlass;

            // BR-3: enabling SSO is the explicit step that transitions onboarding to "enabled"; consent alone
            // never gets here. Disabling SSO steps back to "consented" when a directory was captured, else
            // "not_started" (revert is lossless — the allow-list itself is preserved, FR-8).
            if (effEnabled)
            {
                tenant.SsoOnboardingStatus = SsoOnboardingStatuses.Enabled;
            }
            else if (tenant.SsoOnboardingStatus == SsoOnboardingStatuses.Enabled)
            {
                tenant.SsoOnboardingStatus = effTids.Count > 0
                    ? SsoOnboardingStatuses.Consented
                    : SsoOnboardingStatuses.NotStarted;
            }

            ssoChanged = true;
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

        // US-AUTH-012 FR-7: audit the SSO config change with before/after (no secret material) and drop the
        // per-tenant SSO settings cache so the login/callback path sees the change immediately (NFR-1).
        if (ssoChanged)
        {
            var ssoAfter = new
            {
                tenant.SsoEnabled,
                tenant.AllowedEntraTenantIds,
                tenant.AllowedEmailDomains,
                tenant.JitEnabled,
                tenant.JitDefaultRole,
                EnforcementMode = tenant.SsoEnforcementMode,
                tenant.BreakGlassAdminUserIds,
                tenant.SsoOnboardingStatus,
            };

            await WriteAuditLogWithDetailAsync(
                _currentUser?.UserId,
                "sso_config_updated",
                null,
                null,
                new { before = ssoBefore, after = ssoAfter },
                cancellationToken,
                tenantId);

            // US-AUTH-016 FR-7: a dedicated, queryable enforcement-change audit (with before/after mode) whenever
            // the sign-in enforcement mode changes — including a revert to optional (FR-8/BR-5).
            if (enforcementModeChanged)
            {
                await WriteAuditLogWithDetailAsync(
                    _currentUser?.UserId,
                    "sso_enforcement_changed",
                    null,
                    null,
                    new { before = previousEnforcementMode, after = tenant.SsoEnforcementMode },
                    cancellationToken,
                    tenantId);
            }

            await InvalidateSsoSettingsCacheAsync(tenantId, cancellationToken);
        }

        _logger.LogInformation(
            "Tenant {TenantId} auth settings updated (MFA: {Policy}, Session: idle={Idle}m abs={Abs}h max={Max} strategy={Strategy}, Lockout: maxAttempts={MaxAttempts} duration={LockoutDuration}m progressive={Progressive}, SSO changed={SsoChanged})",
            tenantId, request.MfaPolicy, tenant.IdleTimeoutMinutes, tenant.AbsoluteTimeoutHours,
            tenant.MaxConcurrentSessions, tenant.ConcurrentSessionStrategy,
            tenant.MaxFailedAttempts, tenant.LockoutDurationMinutes, tenant.ProgressiveLockoutEnabled, ssoChanged);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<string>> MarkAdminConsentPendingAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            return Result<string>.Failure("Tenant not found.", 404);
        }

        // Only advance INTO consent_pending from a pre-enable state — never regress an already-enabled tenant.
        if (tenant.SsoOnboardingStatus != SsoOnboardingStatuses.Enabled)
        {
            tenant.SsoOnboardingStatus = SsoOnboardingStatuses.ConsentPending;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await InvalidateSsoSettingsCacheAsync(tenantId, cancellationToken);
        }

        return Result<string>.Success(tenant.Subdomain);
    }

    /// <inheritdoc />
    public async Task<Result> CaptureAdminConsentAsync(
        string subdomain, string customerTid, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(customerTid, out _))
        {
            return Result.Failure("The Microsoft directory id returned by consent was not a valid GUID.", 400);
        }

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain && !t.IsDeleted, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure("Workspace not found.", 404);
        }

        var tid = customerTid.Trim();
        var before = new
        {
            AllowedEntraTenantIds = tenant.AllowedEntraTenantIds ?? [],
            tenant.SsoOnboardingStatus,
        };

        // AC-5/FR-6/BR-3: add the captured directory id to the US-AUTH-012 allow-list (dedup, case-insensitive)
        // and mark onboarding "consented" — WITHOUT enabling SSO (the admin still enables it explicitly). Only
        // step onboarding forward if SSO is not already enabled (never regress an enabled tenant).
        var ids = new List<string>(tenant.AllowedEntraTenantIds ?? []);
        if (!ids.Any(existing => string.Equals(existing, tid, StringComparison.OrdinalIgnoreCase)))
        {
            ids.Add(tid);
        }
        tenant.AllowedEntraTenantIds = ids;

        if (tenant.SsoOnboardingStatus != SsoOnboardingStatuses.Enabled)
        {
            tenant.SsoOnboardingStatus = SsoOnboardingStatuses.Consented;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await WriteAuditLogWithDetailAsync(
            null,
            "sso_admin_consent_completed",
            ipAddress,
            userAgent,
            new
            {
                before,
                after = new { AllowedEntraTenantIds = ids, tenant.SsoOnboardingStatus },
                capturedTid = tid,
            },
            cancellationToken,
            tenant.Id);

        await InvalidateSsoSettingsCacheAsync(tenant.Id, cancellationToken);

        _logger.LogInformation("Admin consent captured for tenant {TenantId} ({Subdomain}); directory id added to allow-list.",
            tenant.Id, subdomain);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RecordAdminConsentFailureAsync(
        string subdomain, string reason, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain && !t.IsDeleted, cancellationToken);

        if (tenant is null)
        {
            // Best-effort: nothing to attribute the failure to. Not an error the caller must surface.
            _logger.LogWarning("Admin consent failure for unknown workspace '{Subdomain}': {Reason}", subdomain, reason);
            return Result.Success();
        }

        // AC-6: prior mode intact — we do NOT enable SSO and do NOT change the enforcement mode. Just audit.
        await WriteAuditLogWithDetailAsync(
            null,
            "sso_admin_consent_failed",
            ipAddress,
            userAgent,
            new { reason, tenant.SsoOnboardingStatus, tenant.SsoEnforcementMode },
            cancellationToken,
            tenant.Id);

        _logger.LogWarning("Admin consent failed for tenant {TenantId} ({Subdomain}): {Reason}. Prior mode intact.",
            tenant.Id, subdomain, reason);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task RecordSsoFailureAsync(
        string eventType, string? subdomain, string reason,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        // Resolve the tenant only from a TRUSTED subdomain (from the signed state). An unknown subdomain, or none
        // at all (state-invalid), yields a null tenantId → a system-level audit row. We never fabricate a tenant.
        Guid? tenantId = null;
        if (!string.IsNullOrWhiteSpace(subdomain))
        {
            var tenant = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Subdomain == subdomain && !t.IsDeleted, cancellationToken);
            tenantId = tenant?.Id;
        }

        // Written directly (not via WriteAuditLogWithDetailAsync) so a system-level failure is recorded with a
        // genuinely null TenantId — the shared helper falls back to the ambient _tenantContext, which would wrongly
        // attribute a state-invalid failure to whatever tenant the callback host happened to resolve to. Detail
        // carries only a non-PII reason code (no tokens/codes/secrets).
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            UserId = null,
            EventType = eventType,
            Detail = JsonSerializer.Serialize(new { reason }),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
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

        // ISSUE-064: only a real unlock should leave an audit trail. If the account was not actually locked
        // (no lockout timestamp and no accumulated failed attempts), clearing the already-clear fields is a
        // no-op — writing account_unlocked_by_admin here would record a phantom "unlocked" event for an
        // account that was never locked. Capture the pre-state before clearing.
        var wasLocked = user.LockedUntil is not null || user.FailedLoginCount > 0;

        if (!wasLocked)
        {
            // Idempotent no-op: nothing to unlock, so no state change and no misleading audit event.
            return Result.Success();
        }

        // Clear lockout state (AC-5)
        user.LockedUntil = null;
        user.FailedLoginCount = 0;
        user.MfaFailedAttemptCount = 0;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Audit: account_unlocked_by_admin (only written when an actual unlock occurred — ISSUE-064).
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
        // ISSUE-334: track whether the USER row itself was JIT-created, so the audit event can distinguish a
        // brand-new identity from an existing user merely gaining a membership. Those are very different events
        // to an auditor — the first creates an account out of an IdP assertion.
        var jitCreatedUser = false;
        if (user is null)
        {
            if (!identity.JitAllowed)
            {
                await WriteAuditLogAsync(null, "sso_login_no_account", identity.IpAddress, identity.UserAgent, cancellationToken, tenant.Id);
                return Result<LoginResponse>.Failure("No HRM account is linked to this Microsoft identity.", 403);
            }

            jitCreatedUser = true;
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

            // ISSUE-334 (US-AUTH-014 AC): auto-provisioning must be distinguishable from an ordinary SSO login
            // in the audit trail. Without this the ONLY record was the Serilog line below, which the in-app
            // audit-search surface cannot read — so an account created straight out of an IdP assertion looked
            // identical to a normal sign-in. Written AFTER the membership commits, so the event never claims a
            // provisioning that did not happen.
            await WriteAuditLogWithDetailAsync(
                user.Id,
                "sso_jit_provisioned",
                identity.IpAddress,
                identity.UserAgent,
                new
                {
                    TenantId = tenant.Id,
                    Email = email,
                    Role = identity.DefaultRole,
                    CreatedUser = jitCreatedUser,
                    IdentityProvider = "entra",
                },
                cancellationToken,
                tenant.Id);

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

        // ISSUE-328: AC-named success event (US-AUTH-011 FR-8 / TC-AUTH-140). Renamed from "sso_login" to match the
        // ACs/TCs — the only consumer was this write site (no other src/, FE, SQL, or test reference).
        await WriteAuditLogAsync(user.Id, "sso_login_succeeded", identity.IpAddress, identity.UserAgent, cancellationToken, tenant.Id);

        return await IssueTokensAsync(user, tenant, userTenant, identity.IpAddress, identity.UserAgent, cancellationToken);
    }

    #region Private Helpers

    /// <summary>
    /// Shared helper for issuing access + refresh tokens after successful authentication.
    /// Called from both LoginAsync and VerifyMfaLoginAsync to avoid duplicating token issuance logic.
    /// </summary>
    /// <summary>Below this many unused recovery codes, the login response nudges the user to regenerate (ISSUE-241).</summary>
    private const int LowRecoveryCodeThreshold = 3;

    private async Task<Result<LoginResponse>> IssueTokensAsync(
        User user,
        Tenant tenant,
        UserTenant userTenant,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken,
        int? recoveryCodesRemaining = null)
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
                // ISSUE-059: record the active-session count + strategy in the audit detail.
                await WriteAuditLogWithDetailAsync(
                    user.Id, "concurrent_session_denied", ipAddress, userAgent,
                    new
                    {
                        activeSessionCount = activeSessions,
                        strategy = tenant.ConcurrentSessionStrategy,
                        maxConcurrentSessions = tenant.MaxConcurrentSessions,
                    },
                    cancellationToken, tenant.Id);
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
                // ISSUE-059: record which session was evicted + the strategy in the audit detail.
                await WriteAuditLogWithDetailAsync(
                    user.Id, "concurrent_session_oldest_revoked", ipAddress, userAgent,
                    new
                    {
                        revokedSessionId = oldestSession.Id,
                        strategy = tenant.ConcurrentSessionStrategy,
                        activeSessionCount = activeSessions,
                    },
                    cancellationToken, tenant.Id);
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
            // ISSUE-241: surfaced only when this login burned a recovery code (else null); flag turns on once the
            // remaining count drops to the low-water mark so the SPA can prompt a regenerate.
            RecoveryCodesRemaining = recoveryCodesRemaining,
            ShouldRegenerateRecoveryCodes =
                recoveryCodesRemaining is not null && recoveryCodesRemaining <= LowRecoveryCodeThreshold,
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
    private sealed record LockoutNotification(
        string Email, string DisplayName, DateTime LockedUntil, int Minutes, string? TenantName = null);

    /// <summary>
    /// ISSUE-063 (US-AUTH-010 FR-8): resolves the login-time tenant's display name for lockout-email branding.
    /// Reads <c>Tenant.Name</c> by the resolved <c>_tenantContext.TenantId</c> (IgnoreQueryFilters/AsNoTracking —
    /// the Tenant row is not itself tenant-scoped). Login can be cross-tenant (per-subdomain), so this is the
    /// tenant the login was ATTEMPTED against. Returns null when unresolved; the email content degrades gracefully.
    /// </summary>
    private async Task<string?> ResolveTenantNameAsync(CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return null;

        return await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

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
