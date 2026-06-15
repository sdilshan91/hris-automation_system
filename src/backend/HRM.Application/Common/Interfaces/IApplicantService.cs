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
}
