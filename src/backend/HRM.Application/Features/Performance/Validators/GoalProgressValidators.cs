using FluentValidation;
using HRM.Application.Features.Performance.Commands;

namespace HRM.Application.Features.Performance.Validators;

/// <summary>
/// Validates the <see cref="AddGoalProgressCommand"/> shape (US-PRF-009 FR-2). The active-cycle window (BR-1),
/// goal ownership (BR-5), the 100%→Completed auto-status (BR-2) and the Blocked notification (BR-3) are
/// persistence/permission concerns enforced in <c>GoalProgressService</c> — putting them here would be a false
/// guarantee.
/// </summary>
public sealed class AddGoalProgressValidator : AbstractValidator<AddGoalProgressCommand>
{
    private const long MaxAttachmentBytes = 10L * 1024 * 1024;

    public AddGoalProgressValidator()
    {
        RuleFor(c => c.Input.GoalId).NotEmpty().WithMessage("A goal is required.");

        RuleFor(c => c.Input.ProgressPct)
            .InclusiveBetween(0, 100).WithMessage("Progress percentage must be between 0 and 100.");

        RuleFor(c => c.Input.Status).IsInEnum().WithMessage("A valid status is required.");

        RuleFor(c => c.Input.Notes)
            .MaximumLength(2000).WithMessage("Notes cannot exceed 2000 characters.");

        RuleFor(c => c.Input.Attachments)
            .Must(a => a == null || a.Count <= 3).WithMessage("A maximum of 3 attachments is allowed.");

        RuleForEach(c => c.Input.Attachments).ChildRules(a =>
        {
            a.RuleFor(x => x.FileName).NotEmpty().WithMessage("Attachment file name is required.")
                .MaximumLength(512).WithMessage("Attachment file name cannot exceed 512 characters.");
            a.RuleFor(x => x.StorageKey).NotEmpty().WithMessage("Attachment storage key is required.")
                .MaximumLength(1024).WithMessage("Attachment storage key cannot exceed 1024 characters.");
            a.RuleFor(x => x.SizeBytes)
                .GreaterThan(0).WithMessage("Attachment size must be greater than 0.")
                .LessThanOrEqualTo(MaxAttachmentBytes).WithMessage("Each attachment must be 10MB or smaller.");
        });
    }
}

/// <summary>Validates the <see cref="AddGoalCommentCommand"/> shape (US-PRF-009 FR-8). Manager/HR authorization is enforced in the service.</summary>
public sealed class AddGoalCommentValidator : AbstractValidator<AddGoalCommentCommand>
{
    public AddGoalCommentValidator()
    {
        RuleFor(c => c.Input.GoalId).NotEmpty().WithMessage("A goal is required.");

        RuleFor(c => c.Input.Body)
            .NotEmpty().WithMessage("Comment body is required.")
            .MaximumLength(500).WithMessage("Comment cannot exceed 500 characters.");
    }
}
