// ============================================================================
// ISSUE-201: employee-directory pagination must reject a negative page/pageSize with a 400,
// not let a negative SQL OFFSET reach Postgres (→ 500 "OFFSET must not be negative").
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.Employees.Queries;
using HRM.Application.Features.Employees.Validators;

namespace HRM.Tests.Unit;

public sealed class GetEmployeesQueryValidatorTests
{
    private readonly GetEmployeesQueryValidator _validator = new();

    [Theory]
    [Trait("Issue", "ISSUE-201")]
    [InlineData(-1)]
    [InlineData(0)] // page is 1-based; page 0 → Skip(-pageSize) too.
    public void NegativeOrZeroPage_IsRejected(int page)
    {
        _validator.TestValidate(new GetEmployeesQuery(Page: page))
            .ShouldHaveValidationErrorFor(x => x.Page);
    }

    [Fact]
    [Trait("Issue", "ISSUE-201")]
    public void NegativePageSize_IsRejected()
    {
        _validator.TestValidate(new GetEmployeesQuery(PageSize: -5))
            .ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Theory]
    [Trait("Issue", "ISSUE-201")]
    [InlineData(1, 20)]
    [InlineData(1, 0)]      // pageSize 0 stays valid — an empty page, not a 400.
    [InlineData(5, 99999)]  // no upper cap is imposed (unchanged behaviour).
    public void ValidPagination_Passes(int page, int pageSize)
    {
        _validator.TestValidate(new GetEmployeesQuery(Page: page, PageSize: pageSize))
            .ShouldNotHaveAnyValidationErrors();
    }
}
