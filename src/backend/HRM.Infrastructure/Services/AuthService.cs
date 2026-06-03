using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Authentication service implementing login, token refresh, logout, password reset, and MFA flows.
/// Handles multi-tenant context, account lockout, and token rotation with reuse detection.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly IJwtService _jwtService;
    private readonly ITenantContext _tenantContext;
    private readonly ITotpService _totpService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext dbContext,
        IJwtService jwtService,
        ITenantContext tenantContext,
        ITotpService totpService,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
        _tenantContext = tenantContext;
        _totpService = totpService;
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
                user.MfaFailedAttemptCount++;

                var maxAttempts = currentTenant.MaxFailedAttempts > 0 ? currentTenant.MaxFailedAttempts : 5;
                if (user.MfaFailedAttemptCount >= maxAttempts)
                {
                    user.LockedUntil = DateTime.UtcNow.AddMinutes(currentTenant.LockoutDurationMinutes > 0 ? currentTenant.LockoutDurationMinutes : 15);
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

        if (user.IsLockedOut)
        {
            return Result<LoginResponse>.Failure(
                "Account temporarily locked. Try again later or contact your administrator.", 401);
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
            // Increment MFA failed attempt counter
            user.MfaFailedAttemptCount++;

            // Determine lockout policy from tenant
            var maxAttempts = 5;
            var lockoutMinutes = 15;
            if (_tenantContext.IsResolved)
            {
                var lockoutTenant = await _dbContext.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, cancellationToken);
                if (lockoutTenant is not null)
                {
                    maxAttempts = lockoutTenant.MaxFailedAttempts > 0 ? lockoutTenant.MaxFailedAttempts : 5;
                    lockoutMinutes = lockoutTenant.LockoutDurationMinutes > 0 ? lockoutTenant.LockoutDurationMinutes : 15;
                }
            }

            if (user.MfaFailedAttemptCount >= maxAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(lockoutMinutes);
                _logger.LogWarning("Account locked for user {UserId} after {Attempts} failed MFA attempts",
                    user.Id, user.MfaFailedAttemptCount);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await WriteAuditLogAsync(user.Id, "mfa_challenge_failure", ipAddress, userAgent, cancellationToken);

            return Result<LoginResponse>.Failure("Invalid verification code.", 401);
        }

        // MFA success - reset counter
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

        await _dbContext.SaveChangesAsync(cancellationToken);
        await WriteAuditLogAsync(null, "tenant_mfa_policy_updated", null, null, cancellationToken, tenantId);

        _logger.LogInformation("Tenant {TenantId} MFA policy updated to {Policy}", tenantId, request.MfaPolicy);

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
                return Result<LoginResponse>.Failure(
                    "Maximum concurrent sessions reached. Please log out from another device.", 403);
            }

            // Revoke oldest session
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

    #endregion
}
