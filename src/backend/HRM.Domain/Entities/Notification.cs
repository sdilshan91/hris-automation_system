namespace HRM.Domain.Entities;

/// <summary>
/// A persisted in-app notification for a single recipient user (US-NTF-001 FR-3, §7).
/// Tenant-scoped via <see cref="BaseEntity"/> (TenantId auto-stamped by TenantInterceptor and
/// read-isolated by the AppDbContext global query filter). Real-time delivery is layered on top
/// via the SignalR <c>NotificationHub</c>; this row is the durable record behind the bell/panel.
/// </summary>
public sealed class Notification : BaseEntity
{
    /// <summary>
    /// Recipient user id (FR-3, BR-1). Maps to the platform user (UserTenant.UserId), not the
    /// employee id, so notifications follow the authenticated user within the tenant.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Machine-readable notification type, e.g. "leave_approved", "task_assigned",
    /// "payroll_completed" (FR-3, §7). Drives the type-specific icon/colour on the client.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Short headline shown in bold for unread items (§7, AC-3).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Brief description shown under the title (§7, AC-3).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional linked-resource type, e.g. "LeaveRequest", "OnboardingTask" (§7). Combined with
    /// <see cref="ResourceId"/> it lets the client deep-link on click (AC-4, FR-8).
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>Optional linked-resource id (§7). String so any id shape (GUID/slug) can be carried.</summary>
    public string? ResourceId { get; set; }

    /// <summary>Read/unread flag (§7). Default false; set true by Mark-as-Read / Mark-All-as-Read (AC-4/AC-5).</summary>
    public bool IsRead { get; set; }

    /// <summary>UTC time the notification was read (AC-4/AC-5). Null while unread.</summary>
    public DateTime? ReadAt { get; set; }
}
