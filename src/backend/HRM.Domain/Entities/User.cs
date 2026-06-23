namespace HRM.Domain.Entities;

/// <summary>
/// Global user entity. A user can belong to multiple tenants via UserTenant.
/// Lockout fields are global (not per-tenant).
/// </summary>
public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public bool MfaEnabled { get; set; }
    public string? MfaSecret { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// CR-AUTH-001 (US-AUTH-014): the Entra <c>oid</c> (immutable object id) this account is linked to,
    /// set on first Microsoft SSO login. Null for local-only accounts. Unique when present — it is the
    /// primary match key for SSO logins (email is the fallback / linking key).
    /// </summary>
    public string? EntraObjectId { get; set; }

    /// <summary>
    /// CR-AUTH-001: identity provider the account authenticates with — "local" (password) or "entra"
    /// (Microsoft SSO). JIT-provisioned SSO users have a null <see cref="PasswordHash"/>.
    /// </summary>
    public string? IdentityProvider { get; set; }

    /// <summary>
    /// Counter for consecutive failed MFA verification attempts.
    /// Resets on successful MFA verification.
    /// </summary>
    public int MfaFailedAttemptCount { get; set; }

    /// <summary>
    /// Number of lockout cycles triggered (for progressive lockout).
    /// Resets when 24 hours pass without a lockout.
    /// </summary>
    public int LockoutCount { get; set; }

    /// <summary>
    /// Timestamp of the most recent lockout event (for progressive lockout window calculation).
    /// </summary>
    public DateTime? LastLockoutAt { get; set; }

    // Navigation
    public ICollection<UserTenant> UserTenants { get; set; } = new List<UserTenant>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<MfaRecoveryCode> MfaRecoveryCodes { get; set; } = new List<MfaRecoveryCode>();

    /// <summary>
    /// Returns true if the account is currently locked out.
    /// </summary>
    public bool IsLockedOut => LockedUntil.HasValue && LockedUntil.Value > DateTime.UtcNow;
}
