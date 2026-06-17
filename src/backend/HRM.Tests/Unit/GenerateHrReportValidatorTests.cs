// ============================================================================
// US-RPT-001: GenerateHrReportQuery validator unit tests.
// Shape-level checks: type must be a known KEBAB report key (§7), date window coherence (from <= to), and the
// employmentTypes / employeeStatuses filter values must be valid enum names. Existence of department/
// location ids in the tenant is enforced by the EF global query filter, not the validator.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Reports.DTOs;
using HRM.Application.Features.Reports.Queries;
using HRM.Application.Features.Reports.Validators;

namespace HRM.Tests.Unit;

public sealed class GenerateHrReportValidatorTests
{
    private readonly GenerateHrReportValidator _validator = new();

    private static GenerateHrReportQuery Query(
        string type = "headcount",
        HrReportQueryParams? qp = null)
        => new(type, qp ?? new HrReportQueryParams());

    [Fact]
    public void Valid_DefaultFilters_Passes()
        => _validator.TestValidate(Query()).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("headcount")]
    [InlineData("turnover")]
    [InlineData("demographics")]
    [InlineData("joiners-leavers")]
    [InlineData("department-distribution")]
    [InlineData("employment-type-breakdown")]
    public void Valid_AllSixReportTypes_AreKnown(string type)
        => _validator.TestValidate(Query(type)).ShouldNotHaveValidationErrorFor(x => x.Type);

    [Theory]
    [InlineData("HEADCOUNT")]
    [InlineData("Joiners-Leavers")]
    public void Valid_KebabKey_IsCaseInsensitive(string type)
        => _validator.TestValidate(Query(type)).ShouldNotHaveValidationErrorFor(x => x.Type);

    [Theory]
    [InlineData("HeadcountSummary")]   // the old PascalCase enum name is no longer accepted on the wire
    [InlineData("nonsense")]
    [InlineData("")]
    public void Invalid_UnknownKebabType_Fails(string type)
        => _validator.TestValidate(Query(type)).ShouldHaveValidationErrorFor(x => x.Type);

    [Fact]
    public void Invalid_DateFromAfterDateTo_Fails()
    {
        var qp = new HrReportQueryParams
        {
            DateFrom = new DateTime(2026, 6, 30),
            DateTo = new DateTime(2026, 6, 1),
        };
        var result = _validator.TestValidate(Query(qp: qp));
        result.ShouldHaveValidationErrorFor("QueryParams.DateFrom");
    }

    [Fact]
    public void Valid_DateFromBeforeDateTo_Passes()
    {
        var qp = new HrReportQueryParams
        {
            DateFrom = new DateTime(2026, 6, 1),
            DateTo = new DateTime(2026, 6, 30),
        };
        _validator.TestValidate(Query(qp: qp)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Invalid_UnknownEmploymentType_Fails()
    {
        var qp = new HrReportQueryParams { EmploymentTypes = ["Freelance"] };
        var result = _validator.TestValidate(Query(qp: qp));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Valid_KnownEmploymentTypeAndStatus_Passes()
    {
        var qp = new HrReportQueryParams
        {
            EmploymentTypes = ["FullTime", "contract"],
            EmployeeStatuses = ["Active", "terminated"],
        };
        _validator.TestValidate(Query(qp: qp)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Invalid_UnknownStatus_Fails()
    {
        var qp = new HrReportQueryParams { EmployeeStatuses = ["Retired"] };
        _validator.TestValidate(Query(qp: qp)).IsValid.Should().BeFalse();
    }
}
