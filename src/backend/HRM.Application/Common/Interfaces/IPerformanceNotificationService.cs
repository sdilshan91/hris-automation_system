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

    /// <summary>
    /// Notifies an employee's manager that the employee submitted their self-assessment (US-PRF-002 AC-2).
    /// Fire-and-forget; must never throw into the request path.
    /// </summary>
    Task NotifySelfAssessmentSubmittedAsync(
        Guid selfAssessmentId,
        Guid employeeId,
        Guid cycleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reminds an employee that their self-assessment is not yet submitted and the deadline is approaching
    /// (US-PRF-002 FR-7/AC-5). Dispatched by the Hangfire reminder job at the configured day thresholds.
    /// </summary>
    Task NotifySelfAssessmentReminderAsync(
        Guid employeeId,
        Guid cycleId,
        int daysUntilDeadline,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies an employee that their manager submitted their performance review (US-PRF-003 AC-2).
    /// Fire-and-forget; must never throw into the request path — the review write is committed regardless.
    /// </summary>
    Task NotifyManagerReviewSubmittedAsync(
        Guid managerReviewId,
        Guid employeeId,
        Guid cycleId,
        CancellationToken cancellationToken = default);
}
