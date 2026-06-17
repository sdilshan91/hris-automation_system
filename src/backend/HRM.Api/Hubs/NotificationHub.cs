using HRM.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HRM.Api.Hubs;

/// <summary>
/// Real-time in-app notification hub (US-NTF-001 AC-1/AC-6, FR-1/FR-2, BR-5). Mapped at
/// <c>/hubs/notifications</c>. JWT-authenticated (the bearer token arrives via the SignalR query string
/// <c>?access_token=…</c> — wired in Program.cs's JwtBearerEvents.OnMessageReceived).
///
/// <para>On connect the user is joined to their tenant-scoped groups:
/// <c>t:{tenantId}:user:{userId}</c> and one <c>t:{tenantId}:role:{role}</c> per role. The tenant id is
/// taken from the resolved <see cref="ITenantContext"/> (or the JWT tenant_id claim as a fallback) — it
/// is NEVER taken from client input, so a client cannot subscribe to another tenant's group (BR-5/AC-6).
/// The hub exposes no client-callable group-join method, which is the other half of rejecting cross-tenant
/// group names at the hub level.</para>
///
/// <para>The server pushes notifications to these groups via <c>IHubContext&lt;NotificationHub&gt;</c>
/// using the client method <c>ReceiveNotification</c> (see <c>SignalRNotificationService</c>).</para>
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    /// <summary>Client method name the server invokes to deliver a notification (pinned FE↔BE contract).</summary>
    public const string ReceiveNotification = "ReceiveNotification";

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(
        ITenantContext tenantContext, ICurrentUser currentUser, ILogger<NotificationHub> logger)
    {
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Group name for a user's connections within a tenant (FR-2). The single source of truth for the
    /// naming convention, reused by the server-side dispatcher.
    /// </summary>
    public static string UserGroup(Guid tenantId, Guid userId) => $"t:{tenantId}:user:{userId}";

    /// <summary>Group name for a role within a tenant (FR-2).</summary>
    public static string RoleGroup(Guid tenantId, string role) => $"t:{tenantId}:role:{role}";

    public override async Task OnConnectedAsync()
    {
        var tenantId = ResolveTenantId();
        var userId = _currentUser.UserId;

        // Defensive: a connection with no resolvable tenant/user cannot be placed in any tenant-scoped
        // group, so it is aborted rather than left floating (BR-5 — no ungrouped/cross-tenant delivery).
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            _logger.LogWarning(
                "NotificationHub connection {ConnectionId} rejected: unresolved tenant/user.", Context.ConnectionId);
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(tenantId, userId));

        foreach (var role in _currentUser.Roles)
        {
            if (!string.IsNullOrWhiteSpace(role))
                await Groups.AddToGroupAsync(Context.ConnectionId, RoleGroup(tenantId, role));
        }

        _logger.LogDebug(
            "NotificationHub: user {UserId} (tenant {TenantId}) connected as {ConnectionId}.",
            userId, tenantId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Resolves the tenant for grouping from the request-scoped tenant context, falling back to the JWT
    /// tenant_id claim. Never trusts client-supplied group names (BR-5).
    /// </summary>
    private Guid ResolveTenantId()
    {
        if (_tenantContext.IsResolved && _tenantContext.TenantId != Guid.Empty)
            return _tenantContext.TenantId;
        return _currentUser.TenantId;
    }
}
