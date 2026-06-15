using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Input payload for submitting an application (US-REC-002 FR-1). The resume is passed as a stream +
/// metadata (mirrors <c>IEmployeeDocumentService.UploadAsync</c>). For an internal submission
/// (<see cref="Source"/> = Internal), <see cref="LinkedEmployeeId"/> must be set (BR-5/FR-8).
/// MIME-type / size / cover-letter validation runs in the FluentValidation validator before the
/// handler; the service re-checks vacancy state (BR-6) and duplicate email (BR-1/AC-3).
/// </summary>
public sealed record SubmitApplicationInput(
    Guid VacancyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? CoverLetter,
    Stream ResumeStream,
    string ResumeFileName,
    string ResumeContentType,
    long ResumeFileSize,
    ApplicationSource Source,
    Guid? LinkedEmployeeId);

/// <summary>
/// Applicant submission + recruiter reads (US-REC-002). All operations are tenant-scoped via
/// ITenantContext and the EF global query filter (AC-5 cross-tenant isolation). Public (anonymous) and
/// internal (authenticated employee) submissions both flow through <see cref="SubmitAsync"/>; the
/// caller sets the source/linked-employee on the input.
/// </summary>
public interface IApplicantService
{
    /// <summary>
    /// Submits an application (AC-1/AC-4). Validates the vacancy is Open + before deadline (BR-6),
    /// rejects a duplicate email for the same vacancy (BR-1/AC-3), virus-scans the resume BEFORE
    /// persisting the storage key (FR-3/NFR-4), stores it under the tenant-scoped path (FR-2/BR-3),
    /// creates the record at stage Applied (FR-6), and fires the confirmation + new-application
    /// notifications (FR-5/FR-7). Returns the confirmation payload.
    /// </summary>
    Task<Result<ApplicationConfirmationDto>> SubmitAsync(SubmitApplicationInput input, CancellationToken cancellationToken = default);

    /// <summary>Gets a single applicant by id (recruiter-facing, tenant-scoped).</summary>
    Task<Result<ApplicantDto>> GetByIdAsync(Guid applicantId, CancellationToken cancellationToken = default);

    /// <summary>Lists applicants for a vacancy (recruiter-facing, paged, tenant-scoped).</summary>
    Task<Result<PagedResult<ApplicantListItemDto>>> ListByVacancyAsync(
        Guid vacancyId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-REC-003 (AC-1/FR-1/FR-5/FR-6): builds the Kanban pipeline board for a vacancy — applicants
    /// grouped into one column per stage with per-stage counts and an overall total, after applying the
    /// optional filters (stage / source / applied-date range / free-text name+email search). Tenant-scoped.
    /// </summary>
    Task<Result<ApplicantPipelineBoardDto>> GetPipelineBoardAsync(
        Guid vacancyId, PipelineFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-REC-003 (AC-3/FR-7): full applicant detail for the slide-over — profile + stage-transition
    /// timeline (BR-5) + resume download reference. Interview data is deferred (US-REC-005/006).
    /// </summary>
    Task<Result<ApplicantDetailDto>> GetDetailAsync(Guid applicantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-REC-003/004 (AC-2/AC-4/FR-3/FR-6/FR-8/BR-2/BR-3/BR-4): moves a single applicant to a new stage,
    /// writing an immutable stage-history row and an audit-log entry, and firing the per-transition
    /// notification (FR-6, log-only seam). Enforces: Rejected requires a structured rejection reason
    /// (AC-4/FR-3) plus the free-text reason (BR-3); a backward move OR moving out of Rejected
    /// (reactivation, BR-2) requires a reason (BR-4); a forward/active move is blocked (409) when the
    /// vacancy is Closed/Cancelled (FR-8). Soft, overridable warnings are returned (never blocking) for
    /// headcount-filled (BR-4) and the stubbed interview/scorecard gates (FR-1/BR-1, pending
    /// US-REC-005/006). Hired is allowed; the convert-to-employee workflow is out of scope (US-REC-010).
    /// Tenant-scoped.
    /// </summary>
    Task<Result<MoveApplicantStageResultDto>> MoveStageAsync(
        Guid applicantId, ApplicantStage toStage, string? reason, string? notes,
        RejectionReason? rejectionReason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// US-REC-003 (FR-8): moves several applicants to the same stage in one action, applying the same
    /// per-applicant rules as <see cref="MoveStageAsync"/>. All-or-nothing: if any applicant fails a
    /// rule the whole batch is rejected so the caller never gets a partial move. Per-applicant warnings
    /// (US-REC-004) are aggregated onto each result.
    /// </summary>
    Task<Result<BulkMoveApplicantStageResultDto>> BulkMoveStageAsync(
        IReadOnlyList<Guid> applicantIds, ApplicantStage toStage, string? reason, string? notes,
        RejectionReason? rejectionReason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// US-REC-003 (AC-3/FR-7/NFR-5): streams the applicant's stored resume through the API (no direct
    /// blob URL). Returns the bytes + original filename + inferred content type. Tenant-scoped; 404 when
    /// the applicant or the stored file is missing.
    /// </summary>
    Task<Result<ResumeDownloadDto>> GetResumeAsync(Guid applicantId, CancellationToken cancellationToken = default);
}
