using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.DTOs;

/// <summary>
/// US-ADM-011c (FR-11 / US-REC-007 FR-10/BR-5): the outcome of an approver's decision on an offer's approval
/// workflow instance. <see cref="Status"/> is "Approved" | "Rejected" | "Pending" (an intermediate step
/// advanced). The offer document itself is unchanged by the decision — Send is gated on the instance being
/// Approved (a separate recruiter action).
/// </summary>
public sealed record OfferApprovalDecisionDto(Guid OfferId, string Status, int? NextStepOrder);

/// <summary>
/// Service-layer input for generating an offer (US-REC-007 FR-1/AC-1). Carried by
/// <c>GenerateOfferCommand</c>. The reference number, version, PDF, and status are server-managed, so they
/// are not part of the input. <see cref="ExpiryDate"/> is optional here — the service defaults it to
/// generation date + 7 days (BR-6) when null.
/// </summary>
public sealed record GenerateOfferInput
{
    public Guid ApplicantId { get; init; }
    public string OfferedPosition { get; init; } = string.Empty;
    public Guid? DepartmentId { get; init; }
    public Guid? ReportingManagerEmployeeId { get; init; }
    public decimal SalaryAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public SalaryFrequency SalaryFrequency { get; init; } = SalaryFrequency.Monthly;

    /// <summary>
    /// Decision D1 (BUG-292): optional tenant-scoped salary structure the compensation is agreed against.
    /// When supplied, the structure must exist in the calling tenant (validated in the service) — carried
    /// through to salary assignment on applicant→employee conversion so the new hire is payroll-ready.
    /// </summary>
    public Guid? SalaryStructureId { get; init; }

    public string? BenefitsSummary { get; init; }
    public DateOnly StartDate { get; init; }

    /// <summary>Optional — defaults to generation date + 7 days when null (BR-6).</summary>
    public DateOnly? ExpiryDate { get; init; }

    public int? ProbationMonths { get; init; }
    public string? CustomClauses { get; init; }
}

/// <summary>
/// Service-layer input for recording an applicant's response to an offer (US-REC-007 AC-3). The recruiter
/// records the response manually (no candidate portal in Phase 1 — DEFERRED).
/// </summary>
public sealed record RespondToOfferInput
{
    /// <summary>True = accepted (advance applicant to Hired, BR-3); false = declined (stays in Offer).</summary>
    public bool Accepted { get; init; }
}

/// <summary>
/// Full offer shape returned to the FE (US-REC-007 FR-1/FR-6). camelCase on the wire. Carries the offer
/// terms, the status (string companion for display), version (FR-9), and the response timestamps.
/// </summary>
public sealed record OfferDto
{
    public Guid Id { get; init; }
    public Guid ApplicantId { get; init; }
    public Guid VacancyId { get; init; }
    public string? VacancyTitle { get; init; }
    public string? ApplicantName { get; init; }

    public string OfferReferenceNumber { get; init; } = string.Empty;

    public OfferStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;

    public string OfferedPosition { get; init; } = string.Empty;
    public Guid? DepartmentId { get; init; }
    public Guid? ReportingManagerEmployeeId { get; init; }

    public decimal SalaryAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public SalaryFrequency SalaryFrequency { get; init; }
    public string SalaryFrequencyName { get; init; } = string.Empty;

    /// <summary>Decision D1 (BUG-292): the salary structure the offer was agreed against, if any.</summary>
    public Guid? SalaryStructureId { get; init; }

    public string? BenefitsSummary { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly ExpiryDate { get; init; }
    public int? ProbationMonths { get; init; }
    public string? CustomClauses { get; init; }

    public int Version { get; init; }

    public DateTime? SentAt { get; init; }
    public DateTime? RespondedAt { get; init; }
    public string? Response { get; init; }

    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// The generated offer-letter PDF bytes + filename + content type, streamed through the API (US-REC-007
/// AC-1/FR-4 preview + download). Mirrors <c>ResumeDownloadDto</c> — no raw blob URL is exposed (NFR-3).
/// </summary>
public sealed record OfferDocumentDto
{
    public byte[] Content { get; init; } = [];
    public string FileName { get; init; } = "offer.pdf";
    public string ContentType { get; init; } = "application/pdf";
}
