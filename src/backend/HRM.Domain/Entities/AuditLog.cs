namespace HRM.Domain.Entities;

/// <summary>
/// Audit log entry for security-relevant events (MFA, login, etc.).
/// Tenant-scoped for per-tenant audit trails.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
