using System.Globalization;
using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-006 Phase 7: the real <see cref="ICoreHrNotificationService"/> (US-CHR-008/009/011). Replaces the final
/// inline "deferred notify" sites — resolves recipients and dispatches Core-HR notifications (email + in-app) via
/// <see cref="INotificationDispatcher"/>. Modelled on <see cref="RealAttendanceNotificationService"/> /
/// <see cref="RealPerformanceNotificationService"/>.
///
/// <para>Recipient rules per method:</para>
/// <list type="bullet">
/// <item><b>probation-ending</b> → the HR pool of the passed tenant id: every ACTIVE tenant user whose roles grant
/// <c>Employee.ChangeStatus</c> (the closest existing HR/employee-admin permission — held by HR Officer / HR Manager
/// / Tenant Admin / Tenant Owner, the roles that action probation transitions).</item>
/// <item><b>manager-reassignment-needed</b> → the same HR pool; the tenant is resolved from the manager's OWN record
/// so it is correct under the request scope AND the system-context future-dated job that also drives it.</item>
/// <item><b>document-expiry</b> → the document-owner employee: resolved to <c>Employee.UserId</c> (in-app + email),
/// falling back to <c>Employee.Email</c> (email-only) when the employee has no linked user.</item>
/// </list>
///
/// <para><b>Cross-tenant note:</b> the probation + document callers run in Hangfire job loops under the system
/// context, iterating items across MANY tenants (each item carries its own <c>TenantId</c>). They pass that tenant
/// id EXPLICITLY; resolution queries use <c>IgnoreQueryFilters</c> scoped to it, mirroring the Hangfire-driven
/// reminders in <see cref="RealPerformanceNotificationService"/>. <b>Never throws</b> — a delivery failure must not
/// break the committed write / scan; every method is wrapped and per-recipient legs are individually guarded.</para>
/// </summary>
public sealed class RealCoreHrNotificationService : ICoreHrNotificationService
{
    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<RealCoreHrNotificationService> _logger;

    public RealCoreHrNotificationService(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        ILogger<RealCoreHrNotificationService> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task NotifyProbationEndingAsync(
        Guid tenantId, Guid employeeId, DateOnly probationEndDate, int daysRemaining,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["title"] = "An employee's probation is ending soon",
                ["message"] =
                    $"{FullName(employee)} (employee no. {employee?.EmployeeNo}) has probation ending on " +
                    $"{Fmt(probationEndDate)} ({daysRemaining} day(s) remaining). Please confirm the transition to " +
                    "Active or extend the probation.",
                ["employee"] = new Dictionary<string, object?>
                {
                    ["firstName"] = employee?.FirstName ?? string.Empty,
                    ["lastName"] = employee?.LastName ?? string.Empty,
                    ["employeeNo"] = employee?.EmployeeNo ?? string.Empty,
                },
                ["probation"] = new Dictionary<string, object?>
                {
                    ["endDate"] = Fmt(probationEndDate),
                    ["daysRemaining"] = daysRemaining,
                },
            });

            var hr = await ResolveHrUserIdsAsync(tenantId, cancellationToken);
            await DispatchToUsersAsync(tenantId, "employee_probation_ending", payload, hr, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "employee_probation_ending", employeeId, tenantId);
        }
    }

    public async Task NotifyManagerReassignmentNeededAsync(
        Guid managerEmployeeId, EmployeeStatus newStatus, int directReportCount,
        CancellationToken cancellationToken = default)
    {
        var tenantId = Guid.Empty;
        try
        {
            // Resolve the manager (and its tenant) by id only — correct in the request scope AND the system-context
            // future-dated job that also drives this via ApplyPendingFutureDatedChangesAsync.
            var manager = await LoadEmployeeByIdAsync(managerEmployeeId, cancellationToken);
            if (manager is null)
            {
                _logger.LogWarning(
                    "RealCoreHrNotificationService: manager {ManagerId} not found for manager_reassignment_needed; " +
                    "nothing dispatched.", managerEmployeeId);
                return;
            }
            tenantId = manager.TenantId;

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["title"] = "A manager's direct reports need reassignment",
                ["message"] =
                    $"{FullName(manager)} has been {newStatus} and has {directReportCount} direct report(s) that " +
                    "need to be reassigned to another manager.",
                ["manager"] = new Dictionary<string, object?>
                {
                    ["firstName"] = manager.FirstName,
                    ["lastName"] = manager.LastName,
                    ["newStatus"] = newStatus.ToString(),
                },
                ["reassignment"] = new Dictionary<string, object?>
                {
                    ["directReportCount"] = directReportCount,
                },
            });

            var hr = await ResolveHrUserIdsAsync(tenantId, cancellationToken);
            await DispatchToUsersAsync(tenantId, "manager_reassignment_needed", payload, hr, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "manager_reassignment_needed", managerEmployeeId, tenantId);
        }
    }

    public async Task NotifyDocumentExpiryAsync(
        Guid tenantId, Guid employeeId, string fileName, string category, DateOnly expiryDate, int daysUntilExpiry,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["title"] = "One of your documents is expiring soon",
                ["message"] =
                    $"Your document \"{fileName}\" ({category}) expires on {Fmt(expiryDate)} " +
                    $"({daysUntilExpiry} day(s) remaining). Please upload a renewed copy.",
                ["employee"] = new Dictionary<string, object?>
                {
                    ["firstName"] = employee?.FirstName ?? string.Empty,
                    ["lastName"] = employee?.LastName ?? string.Empty,
                },
                ["document"] = new Dictionary<string, object?>
                {
                    ["fileName"] = fileName,
                    ["category"] = category,
                    ["expiryDate"] = Fmt(expiryDate),
                    ["daysUntilExpiry"] = daysUntilExpiry,
                },
            });

            await DispatchToEmployeeAsync(tenantId, "document_expiry_warning", payload, employee, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "document_expiry_warning", employeeId, tenantId);
        }
    }

    // ── Recipient resolution ────────────────────────────────────────────────────────────────────────

    private async Task<EmployeeLite?> LoadEmployeeAsync(
        Guid tenantId, Guid employeeId, CancellationToken cancellationToken)
        => await _db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Id == employeeId && e.TenantId == tenantId)
            .Select(e => new EmployeeLite(e.UserId, e.Email, e.FirstName, e.LastName, e.EmployeeNo, e.TenantId))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<EmployeeLite?> LoadEmployeeByIdAsync(Guid employeeId, CancellationToken cancellationToken)
        => await _db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new EmployeeLite(e.UserId, e.Email, e.FirstName, e.LastName, e.EmployeeNo, e.TenantId))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Resolves the distinct ACTIVE tenant users whose assigned roles grant <c>Employee.ChangeStatus</c> (the HR
    /// pool — the closest existing HR/employee-admin permission, held by HR Officer / HR Manager / Tenant Admin /
    /// Tenant Owner). Scoped by tenant id with IgnoreQueryFilters so it is correct outside a request scope; mirrors
    /// the attendance/performance HR-pool queries.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveHrUserIdsAsync(Guid tenantId, CancellationToken cancellationToken)
        => await (
                from ut in _db.UserTenants.IgnoreQueryFilters()
                where ut.TenantId == tenantId && ut.Status == Domain.Entities.UserTenantStatus.Active
                join utr in _db.UserTenantRoles.IgnoreQueryFilters() on ut.Id equals utr.UserTenantId
                join rp in _db.RolePermissions.IgnoreQueryFilters() on utr.RoleId equals rp.RoleId
                where rp.Permission == PermissionCatalog.Employee.ChangeStatus
                select ut.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    // ── Dispatch legs ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dispatches to a single internal employee: in-app + email when the employee has a linked user id, else
    /// email-only against the employee's own address. Per-recipient guarded so a delivery failure can't break the flow.
    /// </summary>
    private async Task DispatchToEmployeeAsync(
        Guid tenantId, string eventKey, string payloadJson, EmployeeLite? recipient, CancellationToken cancellationToken)
    {
        if (recipient is null)
        {
            _logger.LogWarning(
                "RealCoreHrNotificationService: no recipient employee for {EventKey} (tenant {TenantId}); not sent.",
                eventKey, tenantId);
            return;
        }

        try
        {
            if (recipient.UserId is { } userId)
            {
                var request = new NotificationRequest(
                    TenantId: tenantId, EventKey: eventKey, PayloadJson: payloadJson,
                    RecipientUserId: userId, NotificationType: eventKey);
                await _dispatcher.SendInAppAsync(request, cancellationToken);
                await _dispatcher.SendEmailAsync(request, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                var request = new NotificationRequest(
                    TenantId: tenantId, EventKey: eventKey, PayloadJson: payloadJson,
                    RecipientEmail: recipient.Email, NotificationType: eventKey);
                await _dispatcher.SendEmailAsync(request, cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "RealCoreHrNotificationService: recipient for {EventKey} has no user id or email (tenant " +
                    "{TenantId}); not sent.", eventKey, tenantId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RealCoreHrNotificationService: failed to dispatch {EventKey} to an employee (tenant {TenantId}).",
                eventKey, tenantId);
        }
    }

    private async Task DispatchToUsersAsync(
        Guid tenantId, string eventKey, string payloadJson, IReadOnlyList<Guid> recipientUserIds,
        CancellationToken cancellationToken)
    {
        if (recipientUserIds.Count == 0)
        {
            _logger.LogWarning(
                "RealCoreHrNotificationService: no recipients for {EventKey} (tenant {TenantId}); nothing delivered.",
                eventKey, tenantId);
            return;
        }

        foreach (var userId in recipientUserIds.Distinct())
        {
            try
            {
                var request = new NotificationRequest(
                    TenantId: tenantId, EventKey: eventKey, PayloadJson: payloadJson,
                    RecipientUserId: userId, NotificationType: eventKey);
                await _dispatcher.SendInAppAsync(request, cancellationToken);
                await _dispatcher.SendEmailAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "RealCoreHrNotificationService: failed to dispatch {EventKey} to user {UserId}.", eventKey, userId);
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static string Fmt(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FullName(EmployeeLite? e)
        => e is null ? "An employee" : $"{e.FirstName} {e.LastName}".Trim();

    private void LogFailure(Exception ex, string eventKey, Guid subjectId, Guid tenantId)
        => _logger.LogError(ex,
            "RealCoreHrNotificationService: failed to dispatch {EventKey} for {SubjectId} (tenant {TenantId}).",
            eventKey, subjectId, tenantId);

    private sealed record EmployeeLite(
        Guid? UserId, string Email, string FirstName, string LastName, string EmployeeNo, Guid TenantId);
}
