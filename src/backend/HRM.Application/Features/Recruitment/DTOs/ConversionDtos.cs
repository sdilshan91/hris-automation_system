using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.DTOs;

/// <summary>
/// Pre-fill payload for the "Convert to Employee" form (US-REC-010 AC-1/FR-2). Built from the applicant
/// (name/email/phone) plus the most recent accepted offer (position, department, salary, start date,
/// reporting manager, probation) so the HR Officer can review/complete it before creating the employee.
/// All fields are camelCase on the wire. Reference-id companions (departmentName, etc.) let the FE render
/// labels without extra lookups.
/// </summary>
public sealed record ConversionPrefillDto
{
    public Guid ApplicantId { get; init; }
    public Guid VacancyId { get; init; }
    public string? VacancyTitle { get; init; }
    public Guid OfferId { get; init; }
    public string OfferReferenceNumber { get; init; } = string.Empty;

    // From the application (FR-2).
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }

    // From the accepted offer (FR-2).
    public string OfferedPosition { get; init; } = string.Empty;
    public Guid? DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public Guid? ReportingManagerEmployeeId { get; init; }
    public string? ReportingManagerName { get; init; }
    public decimal SalaryAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public SalaryFrequency SalaryFrequency { get; init; }
    public DateOnly StartDate { get; init; }
    public int? ProbationMonths { get; init; }

    // Convenience: the vacancy's job title / location / employment type, so the form can default the
    // structured Core HR fields the offer doesn't carry (the offer only has a free-text position).
    public Guid? JobTitleId { get; init; }
    public string? JobTitleName { get; init; }
    public Guid? LocationId { get; init; }
    public EmploymentType EmploymentType { get; init; }

    /// <summary>True when the applicant has already been converted (FR-10/BR-2) — the FE hides the action.</summary>
    public bool AlreadyConverted { get; init; }
    public Guid? ConvertedToEmployeeId { get; init; }
}

/// <summary>
/// Result of a successful conversion (US-REC-010 AC-2/AC-4). camelCase on the wire. Carries the new
/// employee id + number, the linked applicant, and the updated vacancy filled/headcount ratio so the FE
/// can render the success state and "View Employee Profile" link without a reload.
/// </summary>
public sealed record ConversionResultDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeNo { get; init; } = string.Empty;
    public Guid ApplicantId { get; init; }
    public Guid VacancyId { get; init; }
    public int VacancyFilledCount { get; init; }
    public int VacancyHeadcount { get; init; }
    public bool VacancyClosed { get; init; }

    /// <summary>True when an associated login user account was also created (FR-5). Currently always false
    /// — there is no "auto-create user on hire" tenant setting yet (deferred, see the handler note).</summary>
    public bool UserAccountCreated { get; init; }
}
