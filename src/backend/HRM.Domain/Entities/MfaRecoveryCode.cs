namespace HRM.Domain.Entities;

/// <summary>
/// Represents a single-use MFA recovery code for a user.
/// NOT tenant-scoped -- MFA is global per user.
/// Recovery codes are stored as hashed values and can only be used once.
/// </summary>
public sealed class MfaRecoveryCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;

    /// <summary>
    /// Returns true if this recovery code has not been used yet.
    /// </summary>
    public bool IsAvailable => UsedAt is null;
}
