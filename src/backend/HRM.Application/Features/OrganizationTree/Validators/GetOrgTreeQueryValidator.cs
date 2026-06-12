using FluentValidation;
using HRM.Application.Features.OrganizationTree.Queries;

namespace HRM.Application.Features.OrganizationTree.Validators;

/// <summary>
/// Validates GetOrgTreeQuery parameters (US-CHR-006).
/// </summary>
public sealed class GetOrgTreeQueryValidator : AbstractValidator<GetOrgTreeQuery>
{
    private static readonly HashSet<string> AllowedViews = new(StringComparer.OrdinalIgnoreCase)
    {
        "department", "reporting"
    };

    public GetOrgTreeQueryValidator()
    {
        RuleFor(x => x.View)
            .Must(v => AllowedViews.Contains(v))
            .WithMessage("View must be 'department' or 'reporting'.");

        RuleFor(x => x.Depth)
            .InclusiveBetween(1, 10)
            .WithMessage("Depth must be between 1 and 10.");
    }
}
