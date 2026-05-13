namespace HRM.Domain.Entities;

/// <summary>
/// Represents an organization (tenant) in the multi-tenant HRM platform.
/// </summary>
public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Subdomain { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? ContactEmail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    // Session policy settings
    public int MaxConcurrentSessions { get; set; } = 5;
    public string ConcurrentSessionStrategy { get; set; } = "revoke_oldest"; // "deny_new" | "revoke_oldest"
    public int IdleTimeoutMinutes { get; set; } = 60;
    public int AbsoluteTimeoutHours { get; set; } = 24;

    // Lockout policy settings
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 15;
    public bool ProgressiveLockoutEnabled { get; set; }

    // MFA policy
    public string MfaPolicy { get; set; } = "off"; // "off" | "optional" | "required"

    // Password policy
    public int MinPasswordLength { get; set; } = 12;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireDigit { get; set; } = true;
    public bool RequireSpecialCharacter { get; set; } = true;
    public int PasswordHistoryCount { get; set; } = 5;

    // Navigation
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Role> Roles { get; set; } = new List<Role>();
}

public enum TenantStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Suspended = 3,
    Terminating = 4,
    Terminated = 5
}
