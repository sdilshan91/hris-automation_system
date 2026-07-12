using System.Globalization;
using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-006 Phase 6: the real <see cref="IAttendanceNotificationService"/> (US-ATT-001/004/006). Replaces the
/// inline "deferred notify" sites — resolves recipients and dispatches attendance notifications (email + in-app)
/// via <see cref="INotificationDispatcher"/>. Modelled on <see cref="RealPerformanceNotificationService"/>.
///
/// <para>Recipient rules per method:</para>
/// <list type="bullet">
/// <item><b>late / regularization-decided / overtime-approved / overtime-rejected</b> → the employee.</item>
/// <item><b>regularization-requested / overtime-pre-approval-requested</b> → the employee's
/// <c>ReportsToEmployeeId</c> manager.</item>
/// <item><b>overtime-maxima-exceeded</b> → the attendance-admin/HR pool: every ACTIVE tenant user whose roles
/// grant <c>Attendance.Edit</c> (the closest existing attendance-manage permission).</item>
/// </list>
///
/// <para>Each recipient is resolved to <c>Employee.UserId</c> (in-app + email); when the employee has no linked
/// user we fall back to <c>Employee.Email</c> (email-only). The tenant id comes from <see cref="ITenantContext"/>.
/// <b>Never throws</b> — a delivery failure must not break the committed attendance write; every method is wrapped
/// and per-recipient legs are individually guarded. Resolution queries use <c>IgnoreQueryFilters</c> so they are
/// correct even outside a request scope.</para>
/// </summary>
public sealed class RealAttendanceNotificationService : IAttendanceNotificationService
{
    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<RealAttendanceNotificationService> _logger;

    public RealAttendanceNotificationService(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        ITenantContext tenantContext,
        ILogger<RealAttendanceNotificationService> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task NotifyLateAsync(
        Guid employeeId, DateOnly date, TimeOnly checkIn, TimeOnly? expectedStart,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["title"] = "You were marked late",
                ["message"] =
                    $"Your clock-in on {Fmt(date)} at {Fmt(checkIn)} was after the expected start time of " +
                    $"{(expectedStart is { } e ? Fmt(e) : "your shift start")}.",
                ["employee"] = EmployeeNode(employee),
                ["attendance"] = new Dictionary<string, object?>
                {
                    ["date"] = Fmt(date),
                    ["checkIn"] = Fmt(checkIn),
                    ["expected"] = expectedStart is { } exp ? Fmt(exp) : string.Empty,
                },
            });

            await DispatchToEmployeeAsync(tenantId, "attendance_late", payload, employee, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "attendance_late", employeeId);
        }
    }

    public async Task NotifyRegularizationRequestedAsync(
        Guid employeeId, DateOnly date, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var manager = await LoadManagerAsync(tenantId, employeeId, cancellationToken);

            var payload = RegularizationPayload(
                title: "A regularization request needs your approval",
                message:
                    $"{FullName(employee)} has submitted an attendance regularization request for {Fmt(date)} " +
                    "that needs your approval.",
                employee, date, reason);

            await DispatchToEmployeeAsync(
                tenantId, "attendance_regularization_requested", payload, manager, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "attendance_regularization_requested", employeeId);
        }
    }

    public async Task NotifyRegularizationDecidedAsync(
        bool approved, Guid employeeId, DateOnly date, string reason, CancellationToken cancellationToken = default)
    {
        var eventKey = approved ? "attendance_regularization_approved" : "attendance_regularization_rejected";
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);

            var payload = RegularizationPayload(
                title: approved
                    ? "Your regularization request was approved"
                    : "Your regularization request was rejected",
                message:
                    $"Your attendance regularization request for {Fmt(date)} has been " +
                    (approved ? "approved." : "rejected."),
                employee, date, reason);

            await DispatchToEmployeeAsync(tenantId, eventKey, payload, employee, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, eventKey, employeeId);
        }
    }

    public async Task NotifyOvertimeMaximaExceededAsync(
        Guid employeeId, decimal hours, decimal limit, string period, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["title"] = "Overtime limit exceeded",
                ["message"] =
                    $"Recorded overtime for {FullName(employee)} ({Fmt(hours)} hour(s)) has exceeded the {period} " +
                    $"maximum of {Fmt(limit)} hour(s).",
                ["employee"] = EmployeeNode(employee),
                ["overtime"] = new Dictionary<string, object?>
                {
                    ["hours"] = Fmt(hours), ["limit"] = Fmt(limit), ["period"] = period,
                },
            });

            var hr = await ResolveAttendanceAdminUserIdsAsync(tenantId, cancellationToken);
            await DispatchToUsersAsync(tenantId, "overtime_maxima_exceeded", payload, hr, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "overtime_maxima_exceeded", employeeId);
        }
    }

    public async Task NotifyOvertimePreApprovalRequestedAsync(
        Guid employeeId, DateOnly date, decimal hours, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var manager = await LoadManagerAsync(tenantId, employeeId, cancellationToken);

            var payload = OvertimePayload(
                title: "An overtime pre-approval needs your review",
                message:
                    $"{FullName(employee)} has requested pre-approval for {Fmt(hours)} hour(s) of overtime on " +
                    $"{Fmt(date)}.",
                employee, date, hours, reason: null);

            await DispatchToEmployeeAsync(
                tenantId, "overtime_preapproval_requested", payload, manager, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "overtime_preapproval_requested", employeeId);
        }
    }

    public async Task NotifyOvertimeDecidedAsync(
        bool approved, Guid employeeId, DateOnly date, decimal hours, string? reason,
        CancellationToken cancellationToken = default)
    {
        var eventKey = approved ? "overtime_approved" : "overtime_rejected";
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);

            var payload = OvertimePayload(
                title: approved ? "Your overtime was approved" : "Your overtime was rejected",
                message:
                    $"Your overtime of {Fmt(hours)} hour(s) on {Fmt(date)} has been " +
                    (approved ? "approved." : "rejected."),
                employee, date, hours, reason: approved ? null : reason);

            await DispatchToEmployeeAsync(tenantId, eventKey, payload, employee, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, eventKey, employeeId);
        }
    }

    // ── Recipient resolution ────────────────────────────────────────────────────────────────────────

    private async Task<EmployeeLite?> LoadEmployeeAsync(
        Guid tenantId, Guid employeeId, CancellationToken cancellationToken)
        => await _db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Id == employeeId && e.TenantId == tenantId)
            .Select(e => new EmployeeLite(e.UserId, e.Email, e.FirstName, e.LastName))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Loads the reporting-line manager of <paramref name="employeeId"/> (its ReportsToEmployeeId).</summary>
    private async Task<EmployeeLite?> LoadManagerAsync(
        Guid tenantId, Guid employeeId, CancellationToken cancellationToken)
    {
        var managerId = await _db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Id == employeeId && e.TenantId == tenantId)
            .Select(e => e.ReportsToEmployeeId)
            .FirstOrDefaultAsync(cancellationToken);
        return managerId is { } m ? await LoadEmployeeAsync(tenantId, m, cancellationToken) : null;
    }

    /// <summary>
    /// Resolves the distinct ACTIVE tenant users whose assigned roles grant <c>Attendance.Edit</c> (the closest
    /// existing attendance-manage permission — held by the HR/attendance-admin roles). Scoped by tenant id with
    /// IgnoreQueryFilters so it is correct outside a request scope; mirrors the performance HR-pool query.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveAttendanceAdminUserIdsAsync(
        Guid tenantId, CancellationToken cancellationToken)
        => await (
                from ut in _db.UserTenants.IgnoreQueryFilters()
                where ut.TenantId == tenantId && ut.Status == Domain.Entities.UserTenantStatus.Active
                join utr in _db.UserTenantRoles.IgnoreQueryFilters() on ut.Id equals utr.UserTenantId
                join rp in _db.RolePermissions.IgnoreQueryFilters() on utr.RoleId equals rp.RoleId
                where rp.Permission == PermissionCatalog.Attendance.Edit
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
                "RealAttendanceNotificationService: no recipient employee for {EventKey} (tenant {TenantId}); not sent.",
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
                    "RealAttendanceNotificationService: recipient for {EventKey} has no user id or email (tenant " +
                    "{TenantId}); not sent.", eventKey, tenantId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RealAttendanceNotificationService: failed to dispatch {EventKey} to an employee (tenant {TenantId}).",
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
                "RealAttendanceNotificationService: no recipients for {EventKey} (tenant {TenantId}); nothing delivered.",
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
                    "RealAttendanceNotificationService: failed to dispatch {EventKey} to user {UserId}.", eventKey, userId);
            }
        }
    }

    // ── Payload builders + helpers ──────────────────────────────────────────────────────────────────

    private static string RegularizationPayload(
        string title, string message, EmployeeLite? employee, DateOnly date, string reason) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = title,
            ["message"] = message,
            ["employee"] = EmployeeNode(employee),
            ["attendance"] = new Dictionary<string, object?> { ["date"] = Fmt(date) },
            ["regularization"] = new Dictionary<string, object?> { ["reason"] = reason ?? string.Empty },
        });

    private static string OvertimePayload(
        string title, string message, EmployeeLite? employee, DateOnly date, decimal hours, string? reason)
    {
        var overtime = new Dictionary<string, object?> { ["date"] = Fmt(date), ["hours"] = Fmt(hours) };
        if (reason is not null) overtime["reason"] = reason;
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = title,
            ["message"] = message,
            ["employee"] = EmployeeNode(employee),
            ["overtime"] = overtime,
        });
    }

    private static Dictionary<string, object?> EmployeeNode(EmployeeLite? employee) => new()
    {
        ["firstName"] = employee?.FirstName ?? string.Empty,
        ["lastName"] = employee?.LastName ?? string.Empty,
    };

    private static string Fmt(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Fmt(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Fmt(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FullName(EmployeeLite? e)
        => e is null ? "An employee" : $"{e.FirstName} {e.LastName}".Trim();

    private void LogFailure(Exception ex, string eventKey, Guid subjectId)
        => _logger.LogError(ex,
            "RealAttendanceNotificationService: failed to dispatch {EventKey} for {SubjectId} (tenant {TenantId}).",
            eventKey, subjectId, _tenantContext.TenantId);

    private sealed record EmployeeLite(Guid? UserId, string Email, string FirstName, string LastName);
}
