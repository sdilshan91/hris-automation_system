namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Central in-app notification dispatcher (US-NTF-001 FR-3/FR-4, BR-4). The single seam other modules
/// call to raise a real-time notification: it PERSISTS a <c>notification</c> row (tenant-scoped) and
/// then PUSHES it to the recipient's SignalR group (<c>t:{tenantId}:user:{userId}</c>) via the
/// <c>NotificationHub</c> as the client method <c>ReceiveNotification</c>.
///
/// <para>Tenant id is passed EXPLICITLY so the dispatcher works both inside a request scope and from
/// background jobs / the onboarding outbox worker (which run outside a resolved ITenantContext).
/// Delivery is fire-and-forget from the caller's perspective: the persisted row is the durable record
/// (BR-4) and the SignalR push is best-effort (offline users pick it up from the panel on next load).</para>
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Persist a notification for <paramref name="recipientUserId"/> in <paramref name="tenantId"/> and
    /// push it in real time to that user's SignalR group. Returns the new notification id.
    /// </summary>
    Task<Guid> CreateAndDispatchAsync(
        Guid tenantId,
        Guid recipientUserId,
        string type,
        string title,
        string message,
        string? resourceType = null,
        string? resourceId = null,
        CancellationToken cancellationToken = default);
}
