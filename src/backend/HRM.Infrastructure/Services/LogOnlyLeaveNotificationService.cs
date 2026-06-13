using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="ILeaveNotificationService"/> that emits a structured
/// "leave-requested" log event instead of dispatching a real notification (US-LV-003 seam).
///
/// There is no notification platform yet (FR-6). This keeps the application flow complete and
/// observable. The manager is the employee's reporting line (BR-6, Employee.ReportsToEmployeeId).
/// TODO(notifications): Replace with a queue/notification-service-backed implementation (US-LV-004+).
/// </summary>
public sealed class LogOnlyLeaveNotificationService : ILeaveNotificationService
{
    private readonly ILogger<LogOnlyLeaveNotificationService> _logger;

    public LogOnlyLeaveNotificationService(ILogger<LogOnlyLeaveNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyLeaveRequestedAsync(
        Guid leaveRequestId,
        Guid employeeId,
        Guid? managerEmployeeId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification event {EventType} for leave request {LeaveRequestId}: employee {EmployeeId} -> manager {ManagerEmployeeId}",
            "leave-requested", leaveRequestId, employeeId, managerEmployeeId);

        return Task.CompletedTask;
    }

    public Task NotifyLeaveApprovedAsync(
        Guid leaveRequestId,
        Guid employeeId,
        Guid approverEmployeeId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification event {EventType} for leave request {LeaveRequestId}: approver {ApproverEmployeeId} -> employee {EmployeeId}",
            "leave-approved", leaveRequestId, approverEmployeeId, employeeId);

        return Task.CompletedTask;
    }

    public Task NotifyLeaveRejectedAsync(
        Guid leaveRequestId,
        Guid employeeId,
        Guid approverEmployeeId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification event {EventType} for leave request {LeaveRequestId}: approver {ApproverEmployeeId} -> employee {EmployeeId} (reason: {Reason})",
            "leave-rejected", leaveRequestId, approverEmployeeId, employeeId, reason);

        return Task.CompletedTask;
    }
}
