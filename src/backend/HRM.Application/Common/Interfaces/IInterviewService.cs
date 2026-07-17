using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Interview scheduling, rescheduling, cancellation, and calendar reads (US-REC-005). All operations are
/// tenant-scoped via ITenantContext + the EF global query filter (AC-5 cross-tenant isolation).
/// Scheduling enforces: ≥1 interviewer (FR-1), a future date (BR-3/NFR-6), interviewers must be active
/// employees in the tenant (BR-2), and location/video-link required by interview type (FR-1). Conflict
/// detection (FR-7) returns soft, overridable warnings — never a hard block. Notifications (FR-3/BR-7)
/// and the pre-interview Hangfire reminder (FR-4/BR-6) are fired through the log-only / scheduler seams.
/// </summary>
public interface IInterviewService
{
    /// <summary>
    /// Schedules a new interview (AC-1/FR-1/FR-2). Auto-increments the round number per applicant (FR-2),
    /// links the interviewers, fires participant notifications (FR-3) and schedules the reminder job
    /// (FR-4). Conflict warnings (FR-7) are returned on the result (the interview is still saved).
    /// </summary>
    Task<Result<InterviewDto>> ScheduleAsync(ScheduleInterviewInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reschedules/updates an interview (AC-3/BR-6). Updates the schedule + interviewer set, swaps the
    /// reminder job (delete + recreate) when the timing changes, and re-notifies participants (FR-3).
    /// Conflict warnings (FR-7) are returned on the result.
    /// </summary>
    Task<Result<InterviewDto>> UpdateAsync(Guid interviewId, UpdateInterviewInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an interview (AC-3): sets status Cancelled, deletes the reminder job (BR-6) and notifies
    /// participants (FR-3). Does NOT change the applicant's pipeline stage (BR-4).
    /// </summary>
    Task<Result<InterviewDto>> CancelAsync(Guid interviewId, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-6: records the outcome of a scheduled interview — <see cref="InterviewStatus.Completed"/> or
    /// <see cref="InterviewStatus.NoShow"/>. Only a still-<see cref="InterviewStatus.Scheduled"/> interview
    /// can be concluded; any terminal state is a 409 invalid transition. Clears the reminder job and
    /// notifies participants (FR-3). <paramref name="outcome"/> must be Completed or NoShow (else 400).
    /// </summary>
    Task<Result<InterviewDto>> MarkOutcomeAsync(Guid interviewId, InterviewStatus outcome, CancellationToken cancellationToken = default);

    /// <summary>Gets a single interview by id (tenant-scoped, with interviewer details).</summary>
    Task<Result<InterviewDto>> GetByIdAsync(Guid interviewId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calendar view (FR-5): lists interviews in the tenant, filterable by interviewer / vacancy /
    /// date-range / status. Tenant-scoped.
    /// </summary>
    Task<Result<IReadOnlyList<InterviewDto>>> ListAsync(InterviewCalendarFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Lists all interviews for a single applicant, newest round first (FR-2). Tenant-scoped.</summary>
    Task<Result<IReadOnlyList<InterviewDto>>> ListByApplicantAsync(Guid applicantId, CancellationToken cancellationToken = default);
}
