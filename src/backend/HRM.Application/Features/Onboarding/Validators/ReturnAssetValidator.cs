using FluentValidation;
using HRM.Application.Features.Onboarding.Commands;

namespace HRM.Application.Features.Onboarding.Validators;

/// <summary>US-ONB-005 AC-2 input validation for returning an asset against an offboarding task.</summary>
public sealed class ReturnAssetValidator : AbstractValidator<ReturnAssetCommand>
{
    public ReturnAssetValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty().WithMessage("Task is required.");
        RuleFor(x => x.AssetId).NotEmpty().WithMessage("Asset is required.");
        RuleFor(x => x.Condition).IsInEnum().WithMessage("A valid asset condition is required.");
    }
}
