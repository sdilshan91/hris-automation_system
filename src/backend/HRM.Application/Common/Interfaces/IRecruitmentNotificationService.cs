using HRM.Application.Common.Models;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Dispatches recruitment-related notifications (US-REC-002 FR-5/FR-7).
///
/// <para><b>Which implementation is wired is decided in <c>HRM.Infrastructure/DependencyInjection.cs</c> — check
/// there, not here.</b> As of the US-NTF-006 Phase 5a switch it binds <c>RealRecruitmentNotificationService</c>,
/// which dispatches REAL email and in-app messages via <c>INotificationDispatcher</c>.
/// <c>LogOnlyRecruitmentNotificationService</c> still exists but is only registered explicitly by integration
/// tests. (ISSUE-531: this paragraph previously asserted the log-only service was the default. That claim had been
/// false since Phase 5a, and a reader who trusted it concluded a dropped recruitment notification "costs nothing" —
/// which is how BUG-529 was first filed a severity too low. Restating the composition root in a doc comment is what
/// created the trap, so this now points at the file instead of duplicating it.)</para>
///
/// <para><b>BUG-530 — the contract every method here obeys.</b> Every method returns a <see cref="Result"/> and
/// <b>never throws</b>. The two halves matter equally:</para>
/// <list type="bullet">
/// <item><b>Never throws</b> — a delivery failure must not fail a committed recruitment write. Request-path
/// callers (Applicant/Interview/Scorecard/Offer services) therefore ignore the failure beyond logging it.</item>
/// <item><b>Reports failure</b> — before BUG-530 the whole body was wrapped in a swallow-everything catch, so
/// the method returned an identical completed <c>Task</c> whether every email was delivered or every one
/// failed, and <i>no caller could detect or retry a lost candidate email</i>. A failed <see cref="Result"/>
/// now makes that observable, so the Hangfire reminder jobs can fail their run and let Hangfire's existing
/// automatic-retry machinery re-run the dispatch.</item>
/// </list>
///
/// <para><b>What a failed Result means:</b> at least one dispatch leg raised — i.e. delivery did not happen and
/// a retry may succeed. It is deliberately NOT returned for a recipient with no email address on file: that is
/// a permanent data defect (logged as a warning), and retrying it ten times would only bury the genuinely
/// transient failures in Hangfire's failed-job list.</para>
///
/// <para><b>Delivery guarantee — read this before relying on it.</b> This is <i>retry on a detected failure</i>,
/// NOT at-least-once delivery. It does not survive process death between a successful dispatch and the caller's
/// commit, nor a crash between the state change and the dispatch attempt: nothing durably records that a send
/// is owed. A transactional outbox (a row committed with the state change, drained by a separate worker) would
/// be at-least-once; it was considered and deliberately not built. Do not describe this seam as guaranteed
/// delivery.</para>
/// </summary>
public interface IRecruitmentNotificationService
{
    /// <summary>
    /// Sends the "Application Received" confirmation to the applicant (FR-5, AC-1). Never throws — the
    /// application is committed even if notification dispatch fails; a failed <see cref="Result"/> reports it.
    /// </summary>
    Task<Result> NotifyApplicationReceivedAsync(
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        string applicationReferenceNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies recruiters/hiring team that a new application was received (FR-7). Never throws; a failed
    /// <see cref="Result"/> reports a dispatch failure.
    /// </summary>
    Task<Result> NotifyNewApplicationAsync(
        Guid applicantId,
        Guid vacancyId,
        Guid? hiringManagerEmployeeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the applicant that their pipeline stage changed (US-REC-004 FR-6/NFR-5, AC-1 email).
    /// Never throws — the stage move is committed even if dispatch fails; a failed <see cref="Result"/>
    /// reports it. The async-queue (Hangfire) delivery from NFR-5 is DEFERRED.
    /// </summary>
    Task<Result> NotifyStageChangedAsync(
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        string fromStage,
        string toStage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies all participants (interviewers + the applicant) about an interview lifecycle event
    /// (US-REC-005 FR-3/BR-7) — scheduled, updated/rescheduled or cancelled. Never throws — the interview write
    /// is committed even if dispatch fails; a failed <see cref="Result"/> reports it.
    /// The candidate is external (email-only). Interviewers are resolved from their employee ids to
    /// <c>{UserId, Email}</c>: those with a linked user account get in-app + email; those without fall back
    /// to email-only (ISSUE-263).
    /// </summary>
    /// <param name="eventType">A short event label, e.g. "interview-scheduled" / "interview-updated" / "interview-cancelled".</param>
    /// <param name="applicantEmail">The applicant's email (from their application, BR-7).</param>
    /// <param name="interviewerEmployeeIds">The interviewers' employee ids — the impl resolves each to a linked user (in-app + email) or work email (email-only fallback).</param>
    Task<Result> NotifyInterviewAsync(
        string eventType,
        Guid interviewId,
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        IReadOnlyList<Guid> interviewerEmployeeIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the pre-interview reminder to all participants (US-REC-005 FR-4/AC-2). Invoked by the
    /// Hangfire reminder job ~24h before the interview. Never throws; the job inspects the returned
    /// <see cref="Result"/> and fails its run on a failure so Hangfire retries the dispatch (BUG-530).
    /// Interviewers with a linked user account get in-app + email; those without fall back to email-only
    /// (ISSUE-263). Idempotent (NFR-4).
    /// </summary>
    /// <param name="interviewerEmployeeIds">The interviewers' employee ids (resolved to user/email by the impl).</param>
    Task<Result> NotifyInterviewReminderAsync(
        Guid interviewId,
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        IReadOnlyList<Guid> interviewerEmployeeIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the recruiter / hiring team that an interviewer submitted (or edited) a scorecard
    /// (US-REC-006 FR-5). Never throws — the scorecard write is committed even if dispatch fails; a failed
    /// <see cref="Result"/> reports it.
    /// </summary>
    Task<Result> NotifyScorecardSubmittedAsync(
        Guid scorecardId,
        Guid interviewId,
        Guid applicantId,
        Guid vacancyId,
        Guid interviewerEmployeeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the applicant (and, for expiry, the recruiter) about an offer lifecycle event
    /// (US-REC-007 FR-5/FR-7/FR-8) — sent, withdrawn, expiry-reminder, or expired. Never throws — the offer
    /// write is committed even if dispatch fails; a failed <see cref="Result"/> reports it, which is what lets
    /// <c>OfferExpiryReminderJob</c> / <c>OfferExpiryJob</c> fail their run so Hangfire retries (BUG-530).
    /// </summary>
    /// <param name="eventType">A short event label, e.g. "offer-sent" / "offer-withdrawn" / "offer-expiry-reminder" / "offer-expired".</param>
    /// <param name="applicantEmail">The applicant's email (from their application).</param>
    Task<Result> NotifyOfferAsync(
        string eventType,
        Guid offerId,
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        CancellationToken cancellationToken = default);
}
