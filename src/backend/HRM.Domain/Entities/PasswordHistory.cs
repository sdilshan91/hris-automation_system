namespace HRM.Domain.Entities;

/// <summary>
/// A previously-used password hash for a user (US-AUTH-004 FR-5 / ISSUE-053). Enables password-history
/// enforcement: a reset/change must not reuse any of the last <c>Tenant.PasswordHistoryCount</c> hashes.
/// NOT tenant-scoped — mirrors <see cref="User"/>, which is global (a user can belong to many tenants).
/// </summary>
public sealed class PasswordHistory
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>BCrypt hash of a password the user previously had. Compared via <c>BCrypt.Verify</c> (not reversible).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}
