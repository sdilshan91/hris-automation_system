using FluentValidation;
using HRM.Application.Features.Onboarding.Commands;

namespace HRM.Application.Features.Onboarding.Validators;

/// <summary>
/// Validates task-completion input (US-ONB-003 §7). Covers structural rules enforceable without DB access:
/// task id required, comment length (BR / §7 max 500), and basic attachment shape (size &lt;= 10 MB NFR-6,
/// a file name + content type when a stream is supplied). Ownership (FR-7/BR-1), cannot-revert (BR-3),
/// document-required (AC-4), and MIME allow-list are enforced in the service (returns 403/404/409/400).
/// </summary>
public sealed class CompleteTaskValidator : AbstractValidator<CompleteTaskCommand>
{
    /// <summary>NFR-6: file uploads limited to 10 MB.</summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    public CompleteTaskValidator()
    {
        RuleFor(x => x.TaskInstanceId)
            .NotEmpty().WithMessage("Task instance id is required.");

        RuleFor(x => x.Comment)
            .MaximumLength(500).WithMessage("Comment cannot exceed 500 characters.")
            .When(x => x.Comment is not null);

        // NFR-6: hard size cap (the service also re-checks against the per-stream length).
        RuleFor(x => x.AttachmentSize)
            .LessThanOrEqualTo(MaxFileSizeBytes).WithMessage("File exceeds the 10 MB limit.");

        // When a file is attached it must carry a name + content type (FR-3 MIME validation needs them).
        RuleFor(x => x.AttachmentFileName)
            .NotEmpty().WithMessage("Attachment file name is required when a file is uploaded.")
            .When(x => x.AttachmentStream is not null);

        RuleFor(x => x.AttachmentContentType)
            .NotEmpty().WithMessage("Attachment content type is required when a file is uploaded.")
            .When(x => x.AttachmentStream is not null);
    }
}
