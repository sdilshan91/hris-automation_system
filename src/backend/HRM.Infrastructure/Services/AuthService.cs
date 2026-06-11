using Hangfire;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
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

    public AuthService(
        AppDbContext dbContext,
        IJwtService jwtService,
        ITenantContext tenantContext,
        ITotpService totpService,
        IConfiguration configuration,
        IDistributedCache cache,
        ILogger<AuthService> logger,
        IBackgroundJobClient backgroundJobClient)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
        _tenantContext = tenantContext;
        _totpService = totpService;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
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

                // FR-8/NFR-3: Enqueue lockout notification email via Hangfire
                _backgroundJobClient.Enqueue<ILockoutNotificationService>(
                    svc => svc.SendLockoutNotificationAsync(user.Email, user.DisplayName, user.LockedUntil!.Value, effectiveLockoutMinutes, default));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Login failed: invalid password for user {UserId}, attempt {Attempt}",
                user.Id, user.FailedLoginCount);
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

        if (currentTenant.Status is TenantStatus.Suspended or TenantStatus.Terminated)
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
            if (!string.IsNullOrEmpty(user.MfaSecret) && _totpService.ValidateCode(user.MfaSecret, mfaCode))
            {
                // MFA valid, fall through to token issuance
                user.MfaFailedAttemptCount = 0;
            }
            else
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

                    _backgroundJobClient.Enqueue<ILockoutNotificationService>(
                        svc => svc.SendLockoutNotificationAsync(user.Email, user.DisplayName, user.LockedUntil!.Value, effectiveLockoutMinutes, default));
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await WriteAuditLogAsync(user.Id, "mfa_challenge_failure", ipAddress, userAgent, cancellationToken);
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

        // Check if token was already revoked (reuse detection)
        if (storedToken.RevokedAt is not null)
        {
            // Token reuse detected - revoke entire chain
            _logger.LogWarning("Refresh token reuse detected for user {UserId}, tenant {TenantId}. Revoking all tokens.",
                storedToken.UserId, storedToken.TenantId);

            await RevokeTokenChainAsync(storedToken.UserId, storedToken.TenantId, cancellationToken);
            return Result<RefreshTokenResponse>.Failure("Token reuse detected. All sessions have been revoked.", 401);
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
                // In production: generate reset token and enqueue email via Hangfire
                // For now, log it
                _logger.LogInformation("Password reset requested for user {UserId} in tenant {TenantId}",
                    user.Id, _tenantContext.TenantId);

                // TODO: Generate password reset token using Identity token provider
                // TODO: Enqueue email dispatch via Hangfire background job
            }
        }

        // Always return success regardless of whether user exists
        return Result.Success();
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

        // TODO: Validate token via Identity token provider
        // For now, we accept any non-empty token for the skeleton implementation
        if (string.IsNullOrEmpty(token))
        {
            return Result.Failure("The reset link is invalid or has expired. Please request a new one.", 400);
        }

        // Hash and set new password (BR-2: password reset clears lockout)
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

        await _dbContext.SaveChangesAsync(cancellationToken);

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
        var cacheKey = $"user:{userId}:tenants";
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
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

        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(memberships),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            },
            cancellationToken);

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
            return Result<SwitchTenantResponse>.Failure(
                "You do not have an active membership in this organization.",
                403);
        }

        if (targetTenant.Status is not (TenantStatus.Active or TenantStatus.Trial))
        {
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
            return Result<SwitchTenantResponse>.Failure(
                "You do not have an active membership in this organization.",
                403);
        }

        if (!user.MfaEnabled
            && targetTenant.MfaPolicy == "required"
            && userTenant.UserTenantRoles.Any(utr =>
                targetTenant.MfaRequiredRoles.Contains(utr.Role.Name)))
        {
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

        // TODO: encrypt with pgcrypto KEK once secrets vault is wired (NFR-2)
        user.MfaSecret = secret;
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
        if (!_totpService.ValidateCode(user.MfaSecret, code))
        {
            user.MfaFailedAttemptCount++;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await WriteAuditLogAsync(userId, "mfa_challenge_failure", null, null, cancellationToken);

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
        if (!string.IsNullOrEmpty(user.MfaSecret) && _totpService.ValidateCode(user.MfaSecret, code))
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

                _backgroundJobClient.Enqueue<ILockoutNotificationService>(
                    svc => svc.SendLockoutNotificationAsync(user.Email, user.DisplayName, user.LockedUntil!.Value, effectiveLockoutMinutes, default));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await WriteAuditLogAsync(user.Id, "mfa_challenge_failure", ipAddress, userAgent, cancellationToken);

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
        await WriteAuditLogAsync(userId, eventType, null, null, cancellationToken, tenantId);

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
