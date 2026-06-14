namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Dispatches leave-related notifications to approvers (US-LV-003 FR-6, AC-1).
///
/// SEAM: There is no notification platform yet. The default implementation
/// (<c>LogOnlyLeaveNotificationService</c>) emits a structured "leave-requested" log event
/// instead of sending a real notification, so the application flow is complete and observable.
/// TODO(notifications): Replace with a queue/notification-service-backed implementation
/// (the notification service is asynchronous/fire-and-forget per §10).
/// </summary>
public interface ILeaveNotificationService
{
    /// <summary>
    /// Notifies the employee's reporting manager that a leave request was submitted (AC-1).
    /// Fire-and-forget from the caller's perspective; must never throw into the request path.
    /// </summary>
    Task NotifyLeaveRequestedAsync(
        Guid leaveRequestId,
        Guid employeeId,
        Guid? managerEmployeeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the requesting employee that their leave request was approved (US-LV-005 AC-1,
    /// "leave-approved"). Fire-and-forget; must never throw into the request path (NFR-2 — the
    /// approval is committed even if notification queuing fails, §10).
    /// </summary>
    Task NotifyLeaveApprovedAsync(
        Guid leaveRequestId,
        Guid employeeId,
        Guid approverEmployeeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the requesting employee that their leave request was rejected, with the reason
    /// (US-LV-005 AC-2, "leave-rejected"). Fire-and-forget; must never throw into the request path.
    /// </summary>
    Task NotifyLeaveRejectedAsync(
        Guid leaveRequestId,
        Guid employeeId,
        Guid approverEmployeeId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the employee's reporting manager (and next-level approver if multi-level) that the
    /// employee cancelled their own leave request (US-LV-010 AC-1/AC-2, FR-5, "leave-cancelled").
    /// Fired for both pending and approved cancellations. Fire-and-forget; must never throw into the
    /// request path — the cancellation is committed even if notification queuing fails.
    /// </summary>
    Task NotifyLeaveCancelledAsync(
        Guid leaveRequestId,
        Guid employeeId,
        Guid? managerEmployeeId,
        string? reason,
        CancellationToken cancellationToken = default);
}
