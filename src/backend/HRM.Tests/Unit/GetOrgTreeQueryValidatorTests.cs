// ============================================================================
// US-CHR-006: Validator tests for GetOrgTreeQuery parameters.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.OrganizationTree.Queries;
using HRM.Application.Features.OrganizationTree.Validators;

namespace HRM.Tests.Unit;

public sealed class GetOrgTreeQueryValidatorTests
{
    private readonly GetOrgTreeQueryValidator _validator = new();

    [Theory]
    [InlineData("department")]
    [InlineData("reporting")]
    [InlineData("Department")]
    [InlineData("REPORTING")]
    public void View_ValidValues_ShouldPass(string view)
    {
        var query = new GetOrgTreeQuery(View: view);
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.View);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("tree")]
    [InlineData("org")]
    public void View_InvalidValues_ShouldFail(string view)
    {
        var query = new GetOrgTreeQuery(View: view);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.View);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void Depth_ValidValues_ShouldPass(int depth)
    {
        var query = new GetOrgTreeQuery(Depth: depth);
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.Depth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(100)]
    public void Depth_InvalidValues_ShouldFail(int depth)
    {
        var query = new GetOrgTreeQuery(Depth: depth);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Depth);
    }

    [Fact]
    public void DefaultValues_ShouldPass()
    {
        var query = new GetOrgTreeQuery();
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
