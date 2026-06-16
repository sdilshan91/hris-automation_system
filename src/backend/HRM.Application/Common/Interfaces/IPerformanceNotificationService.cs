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

    /// <summary>
    /// Notifies all participants of a cycle-level event (US-PRF-004 AC-2/AC-4/AC-5/BR-6): phase-start,
    /// deadline-reminder, phase-close, overdue-escalation, cycle-updated, cycle-cancelled. Fire-and-forget;
    /// must never throw into the request path.
    /// </summary>
    /// <param name="eventType">A short event label, e.g. "phase-start" / "deadline-reminder" / "phase-close" / "overdue-escalation" / "cycle-updated" / "cycle-cancelled".</param>
    Task NotifyCycleEventAsync(
        string eventType,
        Guid cycleId,
        Guid employeeId,
        string? detail = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies an assigned 360-degree reviewer that the feedback phase has started and they have a pending
    /// feedback form for a reviewee (US-PRF-005 AC-2). Fire-and-forget; must never throw into the request path.
    /// </summary>
    Task NotifyReviewerAssignedAsync(
        Guid reviewerEmployeeId,
        Guid revieweeEmployeeId,
        Guid cycleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reminds a 360-degree reviewer that they have not yet submitted their feedback and the deadline is
    /// approaching (US-PRF-005 AC-5/FR-8). Dispatched by the Hangfire reminder job. Fire-and-forget.
    /// </summary>
    Task NotifyReviewerReminderAsync(
        Guid reviewerEmployeeId,
        Guid revieweeEmployeeId,
        Guid cycleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies an employee that their manager requested sign-off on the review meeting notes (US-PRF-006 AC-2).
    /// Fire-and-forget; must never throw into the request path.
    /// </summary>
    Task NotifyReviewSignOffRequestedAsync(
        Guid managerReviewId,
        Guid employeeId,
        Guid cycleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the manager and HR that the employee disputed the review (US-PRF-006 AC-3/FR-5). Fire-and-forget.
    /// </summary>
    Task NotifyReviewDisputedAsync(
        Guid managerReviewId,
        Guid employeeId,
        Guid cycleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies HR that a review was auto-closed with No Response because the employee did not sign within the
    /// configured window (US-PRF-006 BR-3). Dispatched by the Hangfire auto-close job. Fire-and-forget.
    /// </summary>
    Task NotifyReviewAutoClosedAsync(
        Guid managerReviewId,
        Guid employeeId,
        Guid cycleId,
        CancellationToken cancellationToken = default);
}
