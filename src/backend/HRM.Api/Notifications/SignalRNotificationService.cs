using HRM.Api.Hubs;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Notifications.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;

namespace HRM.Api.Notifications;

/// <summary>
/// Real <see cref="INotificationService"/> (US-NTF-001 FR-3/FR-4, BR-4). The single dispatcher other modules
/// call to raise an in-app notification: it PERSISTS a tenant-scoped <c>notification</c> row, then PUSHES the
/// resulting <see cref="NotificationDto"/> to the recipient's SignalR group
/// (<c>t:{tenantId}:user:{userId}</c>) via the client method <see cref="NotificationHub.ReceiveNotification"/>.
///
/// <para>It opens its OWN DI scope per call so it works both inside a request scope and from background jobs /
/// the onboarding outbox worker (which run outside a resolved ITenantContext). The persisted row's TenantId is
/// set EXPLICITLY from the passed tenant id (the TenantInterceptor cannot stamp it without a resolved tenant
/// context). The SignalR push is best-effort: a delivery failure never fails the call, since the durable row is
/// already committed (BR-4) and an offline user picks it up from the panel on next load (NFR-5).</para>
/// </summary>
public sealed class SignalRNotificationService : INotificationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(
        IServiceScopeFactory scopeFactory,
        IHubContext<NotificationHub> hubContext,
        ILogger<SignalRNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<Guid> CreateAndDispatchAsync(
        Guid tenantId,
        Guid recipientUserId,
        string type,
        string title,
        string message,
        string? resourceType = null,
        string? resourceId = null,
        CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,            // explicit — dispatcher may run outside a resolved tenant context
            UserId = recipientUserId,
            Type = type,
            Title = title,
            Message = message,
            ResourceType = resourceType,
            ResourceId = resourceId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
        };

        // Persist in a fresh scope so the dispatcher is safe to call from any context (request or job).
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(cancellationToken);
        }

        // Best-effort real-time push (FR-4). A failure here must not undo the durable record (BR-4).
        var dto = new NotificationDto(
            notification.Id, notification.Type, notification.Title, notification.Message,
            notification.ResourceType, notification.ResourceId, notification.IsRead, notification.CreatedAt);

        try
        {
            await _hubContext.Clients
                .Group(NotificationHub.UserGroup(tenantId, recipientUserId))
                .SendAsync(NotificationHub.ReceiveNotification, dto, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SignalR push failed for notification {NotificationId} (tenant {TenantId}, user {UserId}); " +
                "the persisted row will surface on next panel load.",
                notification.Id, tenantId, recipientUserId);
        }

        return notification.Id;
    }
}
