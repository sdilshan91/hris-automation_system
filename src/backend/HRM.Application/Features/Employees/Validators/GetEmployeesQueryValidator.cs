using FluentValidation;
using HRM.Application.Features.Employees.Queries;

namespace HRM.Application.Features.Employees.Validators;

/// <summary>
/// ISSUE-201: guards employee-directory pagination. A negative page/pageSize previously flowed into EF
/// <c>Skip((page-1)*pageSize)</c> as a negative SQL OFFSET, which PostgreSQL rejects ("OFFSET must not be
/// negative") → an unhandled 500. This turns it into a clean 400. (A <c>pageSize</c> of 0 stays valid — it
/// returns an empty page with the correct total count.)
/// </summary>
public sealed class GetEmployeesQueryValidator : AbstractValidator<GetEmployeesQuery>
{
    public GetEmployeesQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be 1 or greater.");

        RuleFor(x => x.PageSize)
            .GreaterThanOrEqualTo(0).WithMessage("Page size cannot be negative.");
    }
}
