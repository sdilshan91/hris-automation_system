using FluentValidation;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.Validators;

/// <summary>
/// Validates a single stage move (US-REC-003 AC-2/BR-3). Structural rules only: a valid target stage,
/// reason mandatory when moving to Rejected (BR-3), and length caps. The backward-move reason rule
/// (BR-4) is stateful — it depends on the applicant's CURRENT stage, which is only known in the service
/// — so it is enforced in ApplicantService.MoveStageAsync, not here.
/// </summary>
public sealed class MoveApplicantStageValidator : AbstractValidator<MoveApplicantStageCommand>
{
    /// <summary>Max length for the reason / notes free-text fields (BR-3/BR-5).</summary>
    public const int MaxReasonLength = 1000;
    public const int MaxNotesLength = 2000;

    public MoveApplicantStageValidator()
    {
        RuleFor(x => x.ApplicantId)
            .NotEmpty().WithMessage("Applicant is required.");

        RuleFor(x => x.ToStage)
            .IsInEnum().WithMessage("A valid target stage is required.");

        // BR-3: moving to Rejected requires a rejection reason.
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required when rejecting an applicant.")
            .When(x => x.ToStage == ApplicantStage.Rejected);

        RuleFor(x => x.Reason)
            .MaximumLength(MaxReasonLength)
            .WithMessage($"Reason cannot exceed {MaxReasonLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Reason));

        RuleFor(x => x.Notes)
            .MaximumLength(MaxNotesLength)
            .WithMessage($"Notes cannot exceed {MaxNotesLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Notes));
    }
}

/// <summary>
/// Validates a bulk stage move (US-REC-003 FR-8/BR-3). Same reason rules as the single move plus a
/// non-empty applicant id list. Backward-move reason (BR-4) is stateful — enforced in the service.
/// </summary>
public sealed class BulkMoveApplicantStageValidator : AbstractValidator<BulkMoveApplicantStageCommand>
{
    public BulkMoveApplicantStageValidator()
    {
        RuleFor(x => x.ApplicantIds)
            .NotEmpty().WithMessage("At least one applicant must be selected.");

        RuleForEach(x => x.ApplicantIds)
            .NotEmpty().WithMessage("Applicant ids must be non-empty.");

        RuleFor(x => x.ToStage)
            .IsInEnum().WithMessage("A valid target stage is required.");

        // BR-3: a bulk move to Rejected requires a reason.
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required when rejecting applicants.")
            .When(x => x.ToStage == ApplicantStage.Rejected);

        RuleFor(x => x.Reason)
            .MaximumLength(MoveApplicantStageValidator.MaxReasonLength)
            .When(x => !string.IsNullOrWhiteSpace(x.Reason));

        RuleFor(x => x.Notes)
            .MaximumLength(MoveApplicantStageValidator.MaxNotesLength)
            .When(x => !string.IsNullOrWhiteSpace(x.Notes));
    }
}
