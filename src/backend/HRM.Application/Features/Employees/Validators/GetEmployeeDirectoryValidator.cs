using FluentValidation;
using HRM.Application.Features.Employees.Queries;

namespace HRM.Application.Features.Employees.Validators;

/// <summary>
/// Validates the GetEmployeeDirectoryQuery parameters (US-CHR-003 FR-5).
/// </summary>
public sealed class GetEmployeeDirectoryValidator : AbstractValidator<GetEmployeeDirectoryQuery>
{
    private static readonly HashSet<int> AllowedPageSizes = [10, 20, 50];
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "employee_no", "date_of_joining", "department"
    };

    public GetEmployeeDirectoryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .Must(ps => AllowedPageSizes.Contains(ps))
            .WithMessage("PageSize must be 10, 20, or 50.");

        RuleFor(x => x.SortBy)
            .Must(sb => sb is null || AllowedSortFields.Contains(sb))
            .WithMessage("SortBy must be one of: name, employee_no, date_of_joining, department.");

        RuleFor(x => x.DateOfJoiningTo)
            .GreaterThanOrEqualTo(x => x.DateOfJoiningFrom)
            .When(x => x.DateOfJoiningFrom.HasValue && x.DateOfJoiningTo.HasValue)
            .WithMessage("DateOfJoiningTo must be on or after DateOfJoiningFrom.");

        RuleFor(x => x.Search)
            .MaximumLength(200)
            .WithMessage("Search query must not exceed 200 characters.");
    }
}
