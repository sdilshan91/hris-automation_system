namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Dispatches recruitment-related notifications (US-REC-002 FR-5/FR-7).
///
/// SEAM: There is no notification/email platform yet. The default implementation
/// (<c>LogOnlyRecruitmentNotificationService</c>) emits structured log events instead of sending real
/// email / in-app notifications, so the application flow is complete and observable — mirrors the
/// <see cref="ILeaveNotificationService"/> log-only seam.
/// TODO(notifications): Replace with a queue/notification-service-backed implementation (the tenant's
/// "Application Received" template per FR-5).
/// </summary>
public interface IRecruitmentNotificationService
{
    /// <summary>
    /// Sends the "Application Received" confirmation to the applicant (FR-5, AC-1). Fire-and-forget;
    /// must never throw into the request path — the application is committed even if notification
    /// dispatch fails.
    /// </summary>
    Task NotifyApplicationReceivedAsync(
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        string applicationReferenceNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies recruiters/hiring team that a new application was received (FR-7). Fire-and-forget;
    /// must never throw into the request path.
    /// </summary>
    Task NotifyNewApplicationAsync(
        Guid applicantId,
        Guid vacancyId,
        Guid? hiringManagerEmployeeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the applicant that their pipeline stage changed (US-REC-004 FR-6/NFR-5, AC-1 email).
    /// Fire-and-forget; must never throw into the request path — the stage move is committed even if
    /// dispatch fails. The async-queue (Hangfire) delivery from NFR-5 is DEFERRED: this is the log-only
    /// seam (mirrors the application-received/new-application events).
    /// </summary>
    Task NotifyStageChangedAsync(
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        string fromStage,
        string toStage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies all participants (interviewers + the applicant) about an interview lifecycle event
    /// (US-REC-005 FR-3/BR-7) — scheduled, updated/rescheduled or cancelled. Fire-and-forget; must never
    /// throw into the request path — the interview write is committed even if dispatch fails.
    /// The candidate is external (email-only). Interviewers are resolved from their employee ids to
    /// <c>{UserId, Email}</c>: those with a linked user account get in-app + email; those without fall back
    /// to email-only (ISSUE-263).
    /// </summary>
    /// <param name="eventType">A short event label, e.g. "interview-scheduled" / "interview-updated" / "interview-cancelled".</param>
    /// <param name="applicantEmail">The applicant's email (from their application, BR-7).</param>
    /// <param name="interviewerEmployeeIds">The interviewers' employee ids — the impl resolves each to a linked user (in-app + email) or work email (email-only fallback).</param>
    Task NotifyInterviewAsync(
        string eventType,
        Guid interviewId,
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        IReadOnlyList<Guid> interviewerEmployeeIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the pre-interview reminder to all participants (US-REC-005 FR-4/AC-2). Invoked by the
    /// Hangfire reminder job ~24h before the interview. Fire-and-forget. Interviewers with a linked user
    /// account get in-app + email; those without fall back to email-only (ISSUE-263). Idempotent (NFR-4).
    /// </summary>
    /// <param name="interviewerEmployeeIds">The interviewers' employee ids (resolved to user/email by the impl).</param>
    Task NotifyInterviewReminderAsync(
        Guid interviewId,
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        IReadOnlyList<Guid> interviewerEmployeeIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the recruiter / hiring team that an interviewer submitted (or edited) a scorecard
    /// (US-REC-006 FR-5). Fire-and-forget; must never throw into the request path — the scorecard write is
    /// committed even if dispatch fails. Real in-app/email delivery is DEFERRED: this is the log-only seam
    /// (mirrors the other recruitment events).
    /// </summary>
    Task NotifyScorecardSubmittedAsync(
        Guid scorecardId,
        Guid interviewId,
        Guid applicantId,
        Guid vacancyId,
        Guid interviewerEmployeeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the applicant (and, for expiry, the recruiter) about an offer lifecycle event
    /// (US-REC-007 FR-5/FR-7/FR-8) — sent, withdrawn, expiry-reminder, or expired. Fire-and-forget; must
    /// never throw into the request/job path — the offer write is committed even if dispatch fails. Real
    /// email (with PDF attachment, FR-5) delivery is DEFERRED: this is the log-only seam (mirrors the
    /// other recruitment events).
    /// </summary>
    /// <param name="eventType">A short event label, e.g. "offer-sent" / "offer-withdrawn" / "offer-expiry-reminder" / "offer-expired".</param>
    /// <param name="applicantEmail">The applicant's email (from their application).</param>
    Task NotifyOfferAsync(
        string eventType,
        Guid offerId,
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        CancellationToken cancellationToken = default);
}
