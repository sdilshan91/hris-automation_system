using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Input for converting an accepted applicant into an employee record (US-REC-010). The applicant's
/// name/email/phone and the accepted offer's position/department/salary/start-date/manager/probation are
/// the pre-filled defaults (FR-2); the HR Officer supplies/overrides the remaining required Core HR fields
/// (structured job title, employment type, location, and optionally a manual employee number — FR-3/AC-2).
/// </summary>
public sealed record ConvertApplicantToEmployeeInput
{
    public Guid ApplicantId { get; init; }

    /// <summary>Structured job title FK (Core HR requires it; the offer only carries a free-text position).</summary>
    public Guid JobTitleId { get; init; }

    /// <summary>Department FK (defaults to the offer's department; required by Core HR).</summary>
    public Guid DepartmentId { get; init; }

    /// <summary>Employment type (FR-3 — not on the offer; the HR Officer selects it).</summary>
    public EmploymentType EmploymentType { get; init; }

    /// <summary>Date of joining (BR-4 — defaults to the offer's start date, overridable).</summary>
    public DateTime DateOfJoining { get; init; }

    /// <summary>Optional reporting manager (defaults to the offer's reporting manager).</summary>
    public Guid? ReportsToEmployeeId { get; init; }

    /// <summary>Optional work location FK (FR-3 — not on the offer).</summary>
    public Guid? LocationId { get; init; }

    /// <summary>Optional manual employee number override (FR-4 — auto-generated when omitted).</summary>
    public string? EmployeeNo { get; init; }

    /// <summary>Optional date of birth (not available from the application; entered manually).</summary>
    public DateTime? DateOfBirth { get; init; }

    /// <summary>Optional gender (not available from the application; entered manually).</summary>
    public Gender? Gender { get; init; }
}

/// <summary>
/// Converts an accepted applicant into a full Core HR employee record (US-REC-010). All operations are
/// tenant-scoped via ITenantContext + the EF global query filter (AC-5). The conversion is atomic (NFR-3):
/// employee creation, applicant linkage, and the vacancy filled-count update either all commit or all roll
/// back. It REUSES the Core HR <see cref="IEmployeeService.CreateAsync"/> path (email uniqueness,
/// department/job-title validation, subscription-plan limit BR-3, auto employee-number FR-4).
/// </summary>
public interface IApplicantConversionService
{
    /// <summary>
    /// Returns the pre-fill payload for the conversion form (AC-1/FR-2): applicant data + the most recent
    /// accepted offer. Fails if the applicant is not eligible (not Hired / no accepted offer). Tenant-scoped.
    /// </summary>
    Task<Result<ConversionPrefillDto>> GetPrefillAsync(Guid applicantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts the applicant to an employee (AC-2). Preconditions (FR-1): the applicant is in the Hired
    /// stage with an Accepted offer. Rejects a duplicate conversion (FR-10/BR-2, 409). Creates the employee
    /// (reusing Core HR create incl. the BR-3 plan-limit check), links the applicant (FR-6), increments the
    /// vacancy filled-count and auto-closes it when full (FR-7/BR-5). Atomic (NFR-3). Tenant-scoped.
    /// </summary>
    Task<Result<ConversionResultDto>> ConvertAsync(ConvertApplicantToEmployeeInput input, CancellationToken cancellationToken = default);
}
