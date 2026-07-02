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
/// <para>Dispatch is best-effort: a notification failure is logged and swallowed so it never fails the
/// originating leave action (approve/reject/cancel/LOP).</para>
/// </summary>
public sealed class LeaveNotificationService : ILeaveNotificationService
{
    private readonly INotificationService _notifications;
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<LeaveNotificationService> _logger;

    public LeaveNotificationService(
        INotificationService notifications,
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<LeaveNotificationService> logger)
    {
        _notifications = notifications;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public Task NotifyLeaveRequestedAsync(
        Guid leaveRequestId, Guid employeeId, Guid? managerEmployeeId,
        CancellationToken cancellationToken = default)
    {
        if (managerEmployeeId is null)
            return Task.CompletedTask;

        return DispatchAsync(
            managerEmployeeId.Value, "leave.requested", "Leave request pending approval",
            "A team member has submitted a leave request that needs your approval.",
            "LeaveRequest", leaveRequestId, cancellationToken);
    }

    public Task NotifyLeaveApprovedAsync(
        Guid leaveRequestId, Guid employeeId, Guid approverEmployeeId,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            employeeId, "leave.approved", "Leave approved",
            "Your leave request has been approved.",
            "LeaveRequest", leaveRequestId, cancellationToken);

    public Task NotifyLeaveRejectedAsync(
        Guid leaveRequestId, Guid employeeId, Guid approverEmployeeId, string reason,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            employeeId, "leave.rejected", "Leave rejected",
            string.IsNullOrWhiteSpace(reason)
                ? "Your leave request has been rejected."
                : $"Your leave request has been rejected. Reason: {reason}",
            "LeaveRequest", leaveRequestId, cancellationToken);

    public Task NotifyLeaveCancelledAsync(
        Guid leaveRequestId, Guid employeeId, Guid? managerEmployeeId, string? reason,
        CancellationToken cancellationToken = default)
    {
        if (managerEmployeeId is null)
            return Task.CompletedTask;

        return DispatchAsync(
            managerEmployeeId.Value, "leave.cancelled", "Leave request cancelled",
            string.IsNullOrWhiteSpace(reason)
                ? "A team member has cancelled a leave request."
                : $"A team member has cancelled a leave request. Reason: {reason}",
            "LeaveRequest", leaveRequestId, cancellationToken);
    }

    public Task NotifyLopAssignedAsync(
        Guid employeeId, string source, decimal dayCount, string? reason,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            employeeId, "leave.lop_assigned", "Loss-of-pay leave assigned",
            string.IsNullOrWhiteSpace(reason)
                ? $"{dayCount} loss-of-pay leave day(s) have been assigned to you ({source})."
                : $"{dayCount} loss-of-pay leave day(s) have been assigned to you ({source}). Reason: {reason}",
            "Employee", employeeId, cancellationToken);

    private async Task DispatchAsync(
        Guid recipientEmployeeId, string type, string title, string message,
        string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        try
        {
            var userId = await _dbContext.Employees
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
    }
}
