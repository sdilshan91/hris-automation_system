using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.DTOs;

/// <summary>
/// Full applicant representation for the submit response and the recruiter detail view (US-REC-002 §7).
/// The resume binary is never returned inline — only its storage key + original file name.
/// </summary>
public sealed record ApplicantDto
{
    public Guid Id { get; init; }
    public Guid VacancyId { get; init; }
    public string? VacancyTitle { get; init; }
    public string ApplicationReferenceNumber { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? CoverLetter { get; init; }
    public string ResumeFileName { get; init; } = string.Empty;
    public string ResumeStorageKey { get; init; } = string.Empty;
    public ApplicantStage Stage { get; init; }
    public string StageName { get; init; } = string.Empty;
    public ApplicationSource Source { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public bool IsInternal { get; init; }
    public Guid? LinkedEmployeeId { get; init; }
    public DateTime AppliedAt { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Optimistic concurrency token (ISSUE-109). Maps to the applicant row's PostgreSQL xmin. Clients
    /// must echo this back on a stage move; a stale value yields 409 Conflict. Mirrors
    /// <c>EmployeeProfileDto.RowVersion</c>.
    /// </summary>
    public uint RowVersion { get; init; }
}

/// <summary>
/// Lightweight applicant row for the recruiter paged list (US-REC-002, recruiter-facing). Omits the
/// cover-letter body and storage key to keep the list payload small.
/// </summary>
public sealed record ApplicantListItemDto
{
    public Guid Id { get; init; }
    public Guid VacancyId { get; init; }
    public string ApplicationReferenceNumber { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string ResumeFileName { get; init; } = string.Empty;
    public ApplicantStage Stage { get; init; }
    public string StageName { get; init; } = string.Empty;
    public ApplicationSource Source { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public bool IsInternal { get; init; }
    public DateTime AppliedAt { get; init; }
}

/// <summary>
/// Confirmation payload returned to the public applicant after a successful submission (AC-1, §8 —
/// "confirmation screen with application reference number"). Deliberately minimal: no internal ids.
/// </summary>
public sealed record ApplicationConfirmationDto
{
    public string ApplicationReferenceNumber { get; init; } = string.Empty;
    public string VacancyTitle { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public DateTime AppliedAt { get; init; }
}

// ── Request bodies (multipart/form-data — resume is a separate IFormFile) ──────────────────────────

/// <summary>
/// Form fields for the anonymous public application submission (AC-1). The resume file is bound
/// separately as an IFormFile on the controller action.
/// </summary>
public sealed record SubmitPublicApplicationRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? CoverLetter { get; init; }
}

/// <summary>
/// Form fields for an authenticated internal application submission (AC-4/FR-8). The resume file is
/// bound separately as an IFormFile on the controller action. <see cref="LinkedEmployeeId"/> links the
/// application to the submitting employee's record (the FE pre-fills the name/email fields).
/// </summary>
public sealed record SubmitInternalApplicationRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? CoverLetter { get; init; }
    public Guid LinkedEmployeeId { get; init; }
}
