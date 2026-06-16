namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Dispatches performance-module notifications (US-PRF-001 AC-2/FR-7).
///
/// SEAM: there is no notification/email platform yet. The default implementation
/// (<c>LogOnlyPerformanceNotificationService</c>) emits structured log events instead of real in-app /
/// email notifications, so the flow is complete and observable — mirrors <see cref="ILeaveNotificationService"/>
/// and <see cref="IRecruitmentNotificationService"/>.
/// TODO(notifications, US-NTF): replace with a real in-app + email notification-service-backed impl.
/// </summary>
public interface IPerformanceNotificationService
{
    /// <summary>
    /// Notifies an employee that their manager assigned or modified a goal (AC-2/FR-7). Fire-and-forget;
    /// must never throw into the request path — the goal write is committed even if dispatch fails.
    /// </summary>
    /// <param name="eventType">A short event label, e.g. "goal-assigned" / "goal-modified" / "goal-removed".</param>
    Task NotifyGoalChangedAsync(
        string eventType,
        Guid goalId,
        Guid employeeId,
        Guid cycleId,
        CancellationToken cancellationToken = default);
}
