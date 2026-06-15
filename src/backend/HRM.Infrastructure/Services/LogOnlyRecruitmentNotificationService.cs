using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IRecruitmentNotificationService"/> that emits structured log events instead of
/// dispatching real email / in-app notifications (US-REC-002 FR-5/FR-7 seam).
///
/// There is no notification/email platform yet. This keeps the application flow complete and
/// observable, mirroring <c>LogOnlyLeaveNotificationService</c>.
/// TODO(notifications): Replace with a queue/notification-service-backed implementation (US-NTF).
/// </summary>
public sealed class LogOnlyRecruitmentNotificationService : IRecruitmentNotificationService
{
    private readonly ILogger<LogOnlyRecruitmentNotificationService> _logger;

    public LogOnlyRecruitmentNotificationService(ILogger<LogOnlyRecruitmentNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyApplicationReceivedAsync(
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        string applicationReferenceNumber,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification event {EventType} for applicant {ApplicantId} on vacancy {VacancyId}: confirmation to {Email} (ref {Ref})",
            "application-received", applicantId, vacancyId, applicantEmail, applicationReferenceNumber);

        return Task.CompletedTask;
    }

    public Task NotifyNewApplicationAsync(
        Guid applicantId,
        Guid vacancyId,
        Guid? hiringManagerEmployeeId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification event {EventType} for vacancy {VacancyId}: new application {ApplicantId} -> hiring manager {HiringManagerEmployeeId}",
            "new-application", vacancyId, applicantId, hiringManagerEmployeeId);

        return Task.CompletedTask;
    }

    public Task NotifyStageChangedAsync(
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        string fromStage,
        string toStage,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification event {EventType} for applicant {ApplicantId} on vacancy {VacancyId}: stage {From} -> {To} (notify {Email})",
            "stage-changed", applicantId, vacancyId, fromStage, toStage, applicantEmail);

        return Task.CompletedTask;
    }

    public Task NotifyInterviewAsync(
        string eventType,
        Guid interviewId,
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        IReadOnlyList<string> interviewerEmails,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification event {EventType} for interview {InterviewId} (applicant {ApplicantId}, vacancy {VacancyId}): notify applicant {Email} + {InterviewerCount} interviewer(s) [{Interviewers}]",
            eventType, interviewId, applicantId, vacancyId, applicantEmail,
            interviewerEmails.Count, string.Join(", ", interviewerEmails));

        return Task.CompletedTask;
    }

    public Task NotifyInterviewReminderAsync(
        Guid interviewId,
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        IReadOnlyList<string> interviewerEmails,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification event {EventType} for interview {InterviewId} (applicant {ApplicantId}, vacancy {VacancyId}): reminder to applicant {Email} + {InterviewerCount} interviewer(s) [{Interviewers}]",
            "interview-reminder", interviewId, applicantId, vacancyId, applicantEmail,
            interviewerEmails.Count, string.Join(", ", interviewerEmails));

        return Task.CompletedTask;
    }

    public Task NotifyScorecardSubmittedAsync(
        Guid scorecardId,
        Guid interviewId,
        Guid applicantId,
        Guid vacancyId,
        Guid interviewerEmployeeId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification event {EventType} for scorecard {ScorecardId} on interview {InterviewId} (applicant {ApplicantId}, vacancy {VacancyId}): interviewer {InterviewerEmployeeId} submitted -> notify recruiter",
            "scorecard-submitted", scorecardId, interviewId, applicantId, vacancyId, interviewerEmployeeId);

        return Task.CompletedTask;
    }

    public Task NotifyOfferAsync(
        string eventType,
        Guid offerId,
        Guid applicantId,
        Guid vacancyId,
        string applicantEmail,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification event {EventType} for offer {OfferId} (applicant {ApplicantId}, vacancy {VacancyId}): notify applicant {Email}",
            eventType, offerId, applicantId, vacancyId, applicantEmail);

        return Task.CompletedTask;
    }
}
