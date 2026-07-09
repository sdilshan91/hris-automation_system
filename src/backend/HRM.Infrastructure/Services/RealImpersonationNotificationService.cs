using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-006 Phase 2a: the real <see cref="IImpersonationNotificationService"/> (US-ADM-003 AC-4/FR-5). Replaces
/// the log-only stub — dispatches the "impersonation started" notification (email + in-app) to the target tenant's
/// admin users via <see cref="INotificationDispatcher"/>. Mandatory / non-suppressible (SecurityAlerts, BR-1): the
/// admin cannot opt out of being told their tenant is being impersonated.
///
/// <para>Runs in the system/admin context (no resolved tenant), so admin resolution reads cross-tenant with
/// IgnoreQueryFilters, explicitly scoped by the target tenant id. <b>Never throws</b> — a delivery failure must not
/// break the impersonation start (every leg is wrapped; exceptions are swallowed + logged), mirroring the LogOnly
/// contract.</para>
/// </summary>
public sealed class RealImpersonationNotificationService : IImpersonationNotificationService
{
    private const string EventKey = "impersonation_started";
    private const string NotificationType = "impersonation.started";

    // The tenant roles whose members are treated as the tenant's admins for security notifications.
    private static readonly string[] AdminRoleNames =
    [
        PermissionCatalog.BuiltInRoles.TenantOwner,
        PermissionCatalog.BuiltInRoles.TenantAdmin,
    ];

    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<RealImpersonationNotificationService> _logger;

    public RealImpersonationNotificationService(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        ILogger<RealImpersonationNotificationService> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task NotifyImpersonationStartedAsync(
        ImpersonationStartedNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            var adminUserIds = await ResolveTenantAdminUserIdsAsync(notification.TargetTenantId, cancellationToken);
            if (adminUserIds.Count == 0)
            {
                _logger.LogWarning(
                    "RealImpersonationNotificationService: no tenant-admin recipients for tenant {TenantId}; " +
                    "impersonation-started notification for session {SessionId} not delivered.",
                    notification.TargetTenantId, notification.SessionId);
                return;
            }

            var payloadJson = BuildPayload(notification);

            foreach (var userId in adminUserIds)
            {
                var request = new NotificationRequest(
                    TenantId: notification.TargetTenantId,
                    EventKey: EventKey,
                    PayloadJson: payloadJson,
                    RecipientUserId: userId,
                    NotificationType: NotificationType);

                // Both legs are internally never-throw; still guard so one recipient's failure can't drop the rest.
                await _dispatcher.SendInAppAsync(request, cancellationToken);
                await _dispatcher.SendEmailAsync(request, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Never throw — the impersonation session is already committed; a notification failure must not surface.
            _logger.LogError(ex,
                "RealImpersonationNotificationService: failed to dispatch impersonation-started notification for " +
                "session {SessionId} (tenant {TenantId}).",
                notification.SessionId, notification.TargetTenantId);
        }
    }

    /// <summary>
    /// Resolves the distinct user ids that hold a tenant-admin role (Tenant Owner / Tenant Admin) via an ACTIVE
    /// membership in the target tenant. System context → IgnoreQueryFilters, explicitly scoped by tenant id.
    /// Resolved in steps (no nested-subquery projection) so the shape is identical on InMemory and PostgreSQL.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveTenantAdminUserIdsAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var memberships = await _db.UserTenants
            .IgnoreQueryFilters()
            .Where(ut => ut.TenantId == tenantId && ut.Status == UserTenantStatus.Active)
            .Select(ut => new { ut.Id, ut.UserId })
            .ToListAsync(cancellationToken);
        if (memberships.Count == 0)
            return [];

        var membershipIds = memberships.Select(m => m.Id).ToList();
        var adminRoleIds = await _db.Roles
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && AdminRoleNames.Contains(r.Name))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        if (adminRoleIds.Count == 0)
            return [];

        var adminMembershipIds = (await _db.UserTenantRoles
                .IgnoreQueryFilters()
                .Where(utr => membershipIds.Contains(utr.UserTenantId) && adminRoleIds.Contains(utr.RoleId))
                .Select(utr => utr.UserTenantId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return memberships
            .Where(m => adminMembershipIds.Contains(m.Id))
            .Select(m => m.UserId)
            .Distinct()
            .ToList();
    }

    private static string BuildPayload(ImpersonationStartedNotification n)
    {
        var startedAt = n.StartedAt.ToString("o");
        var expiresAt = n.ExpiresAt.ToString("o");
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = "A support session has started on your account",
            ["message"] =
                $"Platform support user {n.ImpersonatorEmail} started an impersonation session as " +
                $"{n.TargetUserEmail}. Reason: {n.Reason}",
            ["actor"] = new Dictionary<string, object?> { ["email"] = n.ImpersonatorEmail },
            ["target"] = new Dictionary<string, object?> { ["email"] = n.TargetUserEmail },
            ["reason"] = n.Reason,
            ["session"] = new Dictionary<string, object?> { ["id"] = n.SessionId.ToString() },
            ["startedAt"] = startedAt,
            ["expiresAt"] = expiresAt,
            ["tenant"] = new Dictionary<string, object?> { ["companyName"] = n.TargetTenantName },
        });
    }
}
