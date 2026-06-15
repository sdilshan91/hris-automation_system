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
}
