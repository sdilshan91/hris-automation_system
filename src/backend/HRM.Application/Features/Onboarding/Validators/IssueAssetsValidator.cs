using FluentValidation;
using HRM.Application.Features.Onboarding.Commands;

namespace HRM.Application.Features.Onboarding.Validators;

/// <summary>
/// US-ONB-004 structural validation for bulk asset issuance (§7). Business rules (available gate,
/// double-assignment, per-tenant uniqueness, linked-task ownership) are enforced in the service since they
/// require DB lookups; this validator covers shape: at least one asset, required fields, lengths, and the
/// not-in-the-future issue date.
/// </summary>
public sealed class IssueAssetsValidator : AbstractValidator<IssueAssetsCommand>
{
    public IssueAssetsValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();

        RuleFor(x => x.Assets)
            .NotNull()
            .Must(a => a is { Count: > 0 }).WithMessage("At least one asset must be supplied.");

        RuleForEach(x => x.Assets).SetValidator(new IssueAssetLineValidator());
    }
}

/// <summary>Per-line structural validation for an issued asset (§7).</summary>
public sealed class IssueAssetLineValidator : AbstractValidator<IssueAssetCommandLine>
{
    public IssueAssetLineValidator()
    {
        RuleFor(x => x.AssetType)
            .NotEmpty().WithMessage("Asset type is required.")
            .MaximumLength(50);

        RuleFor(x => x.AssetTag)
            .NotEmpty().WithMessage("Asset tag is required.")
            .MaximumLength(100);

        RuleFor(x => x.SerialNumber).MaximumLength(100);
        RuleFor(x => x.Brand).MaximumLength(100);
        RuleFor(x => x.Model).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);

        RuleFor(x => x.Condition).IsInEnum();

        // §7: issue date cannot be in the future (compared against UTC "today").
        RuleFor(x => x.IssueDate)
            .Must(d => d.Date <= DateTime.UtcNow.Date)
            .WithMessage("Issue date cannot be in the future.");
    }
}
