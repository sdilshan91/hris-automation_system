using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Authentication service implementing login, token refresh, logout, and password reset flows.
/// Handles multi-tenant context, account lockout, and token rotation with reuse detection.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly IJwtService _jwtService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext dbContext,
        IJwtService jwtService,
        ITenantContext tenantContext,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
        _tenantContext = tenantContext;
        _logger = logger;
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
            _logger.LogWarning("Login failed: email {Email} not found", email);
            return Result<LoginResponse>.Failure("Invalid email or password.", 401);
        }

        // 2. Check if account is locked out
        if (user.IsLockedOut)
        {
            _logger.LogWarning("Login failed: account locked for user {UserId}", user.Id);
            return Result<LoginResponse>.Failure(
                "Account temporarily locked. Try again later or contact your administrator.", 401);
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

            // Check tenant lockout policy
            var maxAttempts = 5; // default
            if (_tenantContext.IsResolved)
            {
                var tenant = await _dbContext.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, cancellationToken);
                if (tenant is not null)
                    maxAttempts = tenant.MaxFailedAttempts;
            }

            if (user.FailedLoginCount >= maxAttempts)
            {
                var lockoutMinutes = 15; // default
                if (_tenantContext.IsResolved)
                {
                    var tenant = await _dbContext.Tenants
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, cancellationToken);
                    if (tenant is not null)
                        lockoutMinutes = tenant.LockoutDurationMinutes;
                }

                user.LockedUntil = DateTime.UtcNow.AddMinutes(lockoutMinutes);
                _logger.LogWarning("Account locked for user {UserId} after {Attempts} failed attempts",
                    user.Id, user.FailedLoginCount);
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

            // MFA validation would go here (TOTP verification)
            // For now, placeholder - in production use Otp.NET library
        }

        // 7. Reset failed login count on success
        user.FailedLoginCount = 0;
        user.LockedUntil = null;

        // 8. Gather roles and permissions
        var roles = userTenant.UserTenantRoles
            .Select(utr => utr.Role.Name)
            .ToList();

        var permissions = userTenant.UserTenantRoles
            .SelectMany(utr => utr.Role.RolePermissions)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToList();

        // 9. Generate tokens
        var accessToken = _jwtService.GenerateAccessToken(user, currentTenant.Id, userTenant.Id, roles, permissions);
        var rawRefreshToken = _jwtService.GenerateRefreshToken();
        var refreshTokenHash = _jwtService.HashToken(rawRefreshToken);

        // 10. Check concurrent session limits
        var activeSessions = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .CountAsync(
                rt => rt.UserId == user.Id
                    && rt.TenantId == currentTenant.Id
                    && rt.RevokedAt == null
                    && rt.ExpiresAt > DateTime.UtcNow,
                cancellationToken);

        if (activeSessions >= currentTenant.MaxConcurrentSessions)
        {
            if (currentTenant.ConcurrentSessionStrategy == "deny_new")
            {
                return Result<LoginResponse>.Failure(
                    "Maximum concurrent sessions reached. Please log out from another device.", 403);
            }

            // Revoke oldest session
            var oldestSession = await _dbContext.RefreshTokens
                .IgnoreQueryFilters()
                .Where(rt => rt.UserId == user.Id
                    && rt.TenantId == currentTenant.Id
                    && rt.RevokedAt == null
                    && rt.ExpiresAt > DateTime.UtcNow)
                .OrderBy(rt => rt.IssuedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (oldestSession is not null)
            {
                oldestSession.RevokedAt = DateTime.UtcNow;
            }
        }

        // 11. Store refresh token
        var refreshTokenEntity = new RefreshToken
        {
            Id = BaseEntity.NewUuidV7(),
            UserId = user.Id,
            TenantId = currentTenant.Id,
            TokenHash = refreshTokenHash,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            UserAgent = userAgent,
            IpAddress = ipAddress,
            LastActiveAt = DateTime.UtcNow,
        };

        _dbContext.RefreshTokens.Add(refreshTokenEntity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} logged in to tenant {TenantId}", user.Id, currentTenant.Id);

        return Result<LoginResponse>.Success(new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            User = new UserDto(user.Id, user.Email, user.DisplayName),
            Tenant = new TenantDto(currentTenant.Id, currentTenant.Subdomain, currentTenant.Name),
            Permissions = permissions,
        });
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

        // Check idle timeout
        if (storedToken.LastActiveAt.HasValue)
        {
            var idleMinutes = (DateTime.UtcNow - storedToken.LastActiveAt.Value).TotalMinutes;
            if (idleMinutes > tenant.IdleTimeoutMinutes)
            {
                storedToken.RevokedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result<RefreshTokenResponse>.Failure("Session expired due to inactivity.", 401);
            }
        }

        // Check absolute timeout
        var absoluteHours = (DateTime.UtcNow - storedToken.IssuedAt).TotalHours;
        if (absoluteHours > tenant.AbsoluteTimeoutHours)
        {
            storedToken.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
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

        // Hash and set new password
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        user.PasswordChangedAt = DateTime.UtcNow;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;

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

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("All sessions revoked for user {UserId} in tenant {TenantId}", userId, tenantId);

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

        return Result<CurrentUserDto>.Success(new CurrentUserDto
        {
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Tenant = new TenantDto(tenant.Id, tenant.Subdomain, tenant.Name),
            Roles = roles,
            Permissions = permissions,
            MfaEnabled = user.MfaEnabled,
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
}
