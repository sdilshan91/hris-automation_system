using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// ISSUE-188: the real <see cref="ILeaveNotificationService"/> — it persists + real-time-pushes a
/// notification for each leave lifecycle event via <see cref="INotificationService"/> (US-NTF-001),
/// replacing the log-only seam. Each event resolves its recipient employee to the linked user account
/// and dispatches to that user; employees without a linked user are skipped (log-only).
///
/// <para>US-NTF-006 Phase 3: in addition to the in-app leg, each event now also dispatches an EMAIL via
/// <see cref="INotificationDispatcher.SendEmailAsync"/> to the same recipient (the dispatcher resolves the
/// address from the recipient user + applies the preference gate + renders the tenant's template). The email
/// payload carries the leave placeholders (employee name, leave type, dates, days, reason, approver) that the
/// catalog templates reference.</para>
///
/// <para>Dispatch is best-effort: a notification failure is logged and swallowed so it never fails the
/// originating leave action (approve/reject/cancel/LOP). The in-app and email legs are independently guarded.</para>
/// </summary>
public sealed class LeaveNotificationService : ILeaveNotificationService
{
    private readonly INotificationService _notifications;
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<LeaveNotificationService> _logger;

    // US-NTF-006 Phase 3: the email dispatcher is an OPTIONAL collaborator (DI injects the registered
    // RealNotificationDispatcher; absent → the email leg is skipped, e.g. in unit tests that exercise the
    // in-app leg only). Mirrors the optional-collaborator pattern used elsewhere (e.g. IBackgroundJobClient).
    private readonly INotificationDispatcher? _dispatcher;

    public LeaveNotificationService(
        INotificationService notifications,
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<LeaveNotificationService> logger,
        INotificationDispatcher? dispatcher = null)
    {
        _notifications = notifications;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
        _dispatcher = dispatcher;
    }

    public Task NotifyLeaveRequestedAsync(
        Guid leaveRequestId, Guid employeeId, Guid? managerEmployeeId,
        CancellationToken cancellationToken = default)
    {
        if (managerEmployeeId is null)
            return Task.CompletedTask;

        // Recipient is the approver/manager; the email describes the requesting employee (subject).
        return DispatchAsync(
            managerEmployeeId.Value, "leave.requested", "leave_requested", "Leave request pending approval",
            "A team member has submitted a leave request that needs your approval.",
            "LeaveRequest", leaveRequestId,
            new LeaveEmailInfo(employeeId, leaveRequestId, null, null, null, null), cancellationToken);
    }

    public Task NotifyLeaveApprovedAsync(
        Guid leaveRequestId, Guid employeeId, Guid approverEmployeeId,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            employeeId, "leave.approved", "leave_approved", "Leave approved",
            "Your leave request has been approved.",
            "LeaveRequest", leaveRequestId,
            new LeaveEmailInfo(employeeId, leaveRequestId, approverEmployeeId, null, null, null), cancellationToken);

    public Task NotifyLeaveRejectedAsync(
        Guid leaveRequestId, Guid employeeId, Guid approverEmployeeId, string reason,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            employeeId, "leave.rejected", "leave_rejected", "Leave rejected",
            string.IsNullOrWhiteSpace(reason)
                ? "Your leave request has been rejected."
                : $"Your leave request has been rejected. Reason: {reason}",
            "LeaveRequest", leaveRequestId,
            new LeaveEmailInfo(employeeId, leaveRequestId, approverEmployeeId, reason, null, null), cancellationToken);

    public Task NotifyLeaveCancelledAsync(
        Guid leaveRequestId, Guid employeeId, Guid? managerEmployeeId, string? reason,
        CancellationToken cancellationToken = default)
    {
        if (managerEmployeeId is null)
            return Task.CompletedTask;

        // Recipient is the manager; the email describes the employee who cancelled (subject).
        return DispatchAsync(
            managerEmployeeId.Value, "leave.cancelled", "leave_cancelled", "Leave request cancelled",
            string.IsNullOrWhiteSpace(reason)
                ? "A team member has cancelled a leave request."
                : $"A team member has cancelled a leave request. Reason: {reason}",
            "LeaveRequest", leaveRequestId,
            new LeaveEmailInfo(employeeId, leaveRequestId, null, reason, null, null), cancellationToken);
    }

    public Task NotifyLopAssignedAsync(
        Guid employeeId, string source, decimal dayCount, string? reason,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            employeeId, "leave.lop_assigned", "leave_lop", "Loss-of-pay leave assigned",
            string.IsNullOrWhiteSpace(reason)
                ? $"{dayCount} loss-of-pay leave day(s) have been assigned to you ({source})."
                : $"{dayCount} loss-of-pay leave day(s) have been assigned to you ({source}). Reason: {reason}",
            "Employee", employeeId,
            new LeaveEmailInfo(employeeId, null, null, reason, dayCount, source), cancellationToken);

    /// <summary>
    /// Resolves the recipient employee's linked user id, then dispatches the in-app notification (unchanged) and,
    /// when a user is resolved, the email notification (US-NTF-006 Phase 3). The two legs are independently guarded
    /// so a failure in one — or in the email payload build — never breaks the other or the originating leave flow.
    /// </summary>
    private async Task DispatchAsync(
        Guid recipientEmployeeId, string type, string eventKey, string title, string message,
        string resourceType, Guid resourceId, LeaveEmailInfo email, CancellationToken cancellationToken)
    {
        Guid? userId = null;
        try
        {
            userId = await _dbContext.Employees
                .Where(e => e.Id == recipientEmployeeId)
                .Select(e => e.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (userId is null || userId == Guid.Empty)
            {
                _logger.LogDebug(
                    "Leave notification {Type} skipped: employee {EmployeeId} has no linked user account",
                    type, recipientEmployeeId);
                return;
            }

            await _notifications.CreateAndDispatchAsync(
                _tenantContext.TenantId, userId.Value, type, title, message,
                resourceType, resourceId.ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Leave notification {Type} for employee {EmployeeId} failed (non-fatal)",
                type, recipientEmployeeId);
        }

        // Email leg (US-NTF-006 Phase 3) — only when a dispatcher is wired AND a recipient user resolved (the
        // dispatcher looks up the address from that user). Fully independent of the in-app leg; never throws.
        if (_dispatcher is not null && userId is Guid uid && uid != Guid.Empty)
            await SendEmailAsync(_dispatcher, uid, eventKey, title, message, email, cancellationToken);
    }

    /// <summary>
    /// Builds the leave email payload and dispatches it via <see cref="INotificationDispatcher.SendEmailAsync"/>.
    /// Wrapped so a payload-build or dispatch failure is logged and swallowed (the dispatcher is itself never-throw).
    /// </summary>
    private async Task SendEmailAsync(
        INotificationDispatcher dispatcher, Guid recipientUserId, string eventKey, string title, string message,
        LeaveEmailInfo info, CancellationToken cancellationToken)
    {
        try
        {
            var payloadJson = await BuildEmailPayloadAsync(title, message, info, cancellationToken);
            var request = new NotificationRequest(
                TenantId: _tenantContext.TenantId,
                EventKey: eventKey,
                PayloadJson: payloadJson,
                RecipientUserId: recipientUserId,
                NotificationType: null); // defaults to the event key on the delivery-audit row.

            await dispatcher.SendEmailAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Leave notification email {EventKey} for user {UserId} failed (non-fatal)",
                eventKey, recipientUserId);
        }
    }

    /// <summary>
    /// Assembles the JSON payload whose nested fields fill the catalog leave-email templates: the subject employee
    /// (the person the leave belongs to), the leave request details (type/dates/days/reason) or the LOP details
    /// (days/source), the approver name (when applicable), and the tenant company name for branding. Names are
    /// resolved with separate scalar lookups (not navigation projections) so the shape is identical on
    /// InMemory and PostgreSQL.
    /// </summary>
    private async Task<string> BuildEmailPayloadAsync(
        string title, string message, LeaveEmailInfo info, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == info.SubjectEmployeeId)
            .Select(e => new { e.FirstName, e.LastName, e.Email })
            .FirstOrDefaultAsync(cancellationToken);

        var leaveNode = new Dictionary<string, object?>();

        if (info.LeaveRequestId is Guid leaveRequestId)
        {
            var lr = await _dbContext.LeaveRequests
                .AsNoTracking()
                .Where(l => l.Id == leaveRequestId)
                .Select(l => new
                {
                    l.LeaveTypeId, l.StartDate, l.EndDate, l.TotalDays, l.Reason, l.CancellationReason,
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (lr is not null)
            {
                var typeName = await _dbContext.LeaveTypes
                    .AsNoTracking()
                    .Where(t => t.Id == lr.LeaveTypeId)
                    .Select(t => t.Name)
                    .FirstOrDefaultAsync(cancellationToken);

                leaveNode["type"] = typeName;
                leaveNode["startDate"] = lr.StartDate.ToString("yyyy-MM-dd");
                leaveNode["endDate"] = lr.EndDate.ToString("yyyy-MM-dd");
                leaveNode["days"] = lr.TotalDays;
                leaveNode["reason"] = FirstNonBlank(info.Reason, lr.CancellationReason, lr.Reason);
            }
        }

        if (info.LopDays is decimal lopDays)
        {
            leaveNode["days"] = lopDays;
            leaveNode["source"] = info.LopSource;
        }

        if (!leaveNode.ContainsKey("reason"))
            leaveNode["reason"] = info.Reason;

        var tenantName = await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var payload = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["message"] = message,
            ["employee"] = new Dictionary<string, object?>
            {
                ["firstName"] = employee?.FirstName,
                ["lastName"] = employee?.LastName,
                ["email"] = employee?.Email,
            },
            ["leave"] = leaveNode,
            ["tenant"] = new Dictionary<string, object?>
            {
                ["name"] = tenantName,
                ["companyName"] = tenantName,
            },
        };

        if (info.ApproverEmployeeId is Guid approverId)
        {
            var approver = await _dbContext.Employees
                .AsNoTracking()
                .Where(e => e.Id == approverId)
                .Select(e => new { e.FirstName, e.LastName })
                .FirstOrDefaultAsync(cancellationToken);
            if (approver is not null)
                payload["approver"] = new Dictionary<string, object?>
                {
                    ["name"] = $"{approver.FirstName} {approver.LastName}".Trim(),
                };
        }

        return JsonSerializer.Serialize(payload);
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// The data an email dispatch needs beyond the resolved recipient: the SUBJECT employee (whose leave it is),
    /// the leave request id (null for LOP), the approver (approved/rejected), a free-text reason, and the LOP
    /// day-count + source (LOP only).
    /// </summary>
    private sealed record LeaveEmailInfo(
        Guid SubjectEmployeeId,
        Guid? LeaveRequestId,
        Guid? ApproverEmployeeId,
        string? Reason,
        decimal? LopDays,
        string? LopSource);
}
