using FluentValidation;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.Validators;

/// <summary>
/// Validates an application submission (US-REC-002 FR-1, AC-2, BR-4). Structural rules only — required
/// name/email, a valid email, cover-letter length (BR-1 ≤2000), resume MIME type (BR-4) and size
/// (AC-2 ≤25 MB), and (for an internal submission) a linked employee id. Stateful rules — vacancy must
/// be Open + before deadline (BR-6) and duplicate-email rejection (BR-1/AC-3) — are enforced in the
/// service, where the DbContext is available.
/// </summary>
public sealed class SubmitApplicationValidator : AbstractValidator<SubmitApplicationCommand>
{
    /// <summary>Allowed resume MIME types (BR-4): PDF, DOCX, DOC.</summary>
    public static readonly IReadOnlySet<string> AllowedResumeMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // .docx
        "application/msword",                                                       // .doc
    };

    /// <summary>Maximum resume size: 25 MB (AC-2, FR-1).</summary>
    public const long MaxResumeSizeBytes = 25L * 1024 * 1024;

    /// <summary>Maximum cover-letter length (FR-1, BR-1).</summary>
    public const int MaxCoverLetterLength = 2000;

    public SubmitApplicationValidator()
    {
        RuleFor(x => x.VacancyId)
            .NotEmpty().WithMessage("Vacancy is required.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name cannot exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name cannot exceed 100 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256).WithMessage("Email cannot exceed 256 characters.");

        RuleFor(x => x.Phone)
            .MaximumLength(40).WithMessage("Phone cannot exceed 40 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.CoverLetter)
            .MaximumLength(MaxCoverLetterLength)
            .WithMessage($"Cover letter cannot exceed {MaxCoverLetterLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.CoverLetter));

        // FR-1: resume is required.
        RuleFor(x => x.ResumeFileName)
            .NotEmpty().WithMessage("A resume file is required.");

        RuleFor(x => x.ResumeFileSize)
            .GreaterThan(0).WithMessage("A resume file is required.")
            .LessThanOrEqualTo(MaxResumeSizeBytes)
            .WithMessage("Resume exceeds the 25 MB limit.");

        // BR-4: only PDF / DOCX / DOC are accepted.
        RuleFor(x => x.ResumeContentType)
            .Must(ct => ct is not null && AllowedResumeMimeTypes.Contains(ct))
            .WithMessage("Resume must be a PDF, DOC, or DOCX file.");

        // FR-8/BR-5: an internal application must be linked to an employee record.
        RuleFor(x => x.LinkedEmployeeId)
            .NotNull().NotEqual(Guid.Empty)
            .WithMessage("An internal application must be linked to an employee.")
            .When(x => x.Source == ApplicationSource.Internal);

        // Unknown is a read-only sentinel for a corrupt DB row (ISSUE-231); it must never be submitted.
        RuleFor(x => x.Source)
            .NotEqual(ApplicationSource.Unknown).WithMessage("A valid application source is required.");
    }
}
