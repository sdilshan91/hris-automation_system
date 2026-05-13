namespace HRM.Domain.Entities;

/// <summary>
/// Represents a refresh token for a user-tenant session.
/// Tokens are stored as SHA-256 hashes; raw tokens are never persisted.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
    public DateTime? LastActiveAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public RefreshToken? ReplacedByToken { get; set; }

    /// <summary>
    /// Returns true if this token is currently active (not revoked and not expired).
    /// </summary>
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}
