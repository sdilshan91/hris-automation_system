// ============================================================================
// US-LV-001: Leave Type Validator Tests
// Tests FluentValidation rules for CreateLeaveTypeCommand and
// UpdateLeaveTypeCommand: name required + length, color hex format,
// entitlement >= 0, accrual frequency enum, gender enum, day thresholds.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.LeaveTypes.Commands;
using HRM.Application.Features.LeaveTypes.Validators;

namespace HRM.Tests.Unit;

public sealed class LeaveTypeValidatorTests
{
    private readonly CreateLeaveTypeValidator _createValidator = new();
    private readonly UpdateLeaveTypeValidator _updateValidator = new();

    private static CreateLeaveTypeCommand MakeCreateCommand(
        string name = "Annual Leave",
        string? color = null,
        decimal entitlement = 14,
        string accrualFrequency = "Monthly",
        string gender = "All",
        int? maxConsecutiveDays = null,
        int? documentDayThreshold = null,
        decimal? maxEncashDays = null,
        decimal? negativeBalanceLimit = null,
        decimal? carryForwardLimit = null,
        int? carryForwardExpiryMonths = null) => new(
        Name: name,
        Code: "AL",
        Color: color,
        Description: "Test",
        AnnualEntitlement: entitlement,
        AccrualFrequency: accrualFrequency,
        CarryForwardLimit: carryForwardLimit,
        CarryForwardExpiryMonths: carryForwardExpiryMonths,
        ProbationEligible: false,
        DocumentsRequired: false,
        DocumentDayThreshold: documentDayThreshold,
        Encashable: false,
        MaxEncashDays: maxEncashDays,
        HalfDayAllowed: true,
        HourlyAllowed: false,
        Gender: gender,
        MaxConsecutiveDays: maxConsecutiveDays,
        NegativeBalanceAllowed: false,
        NegativeBalanceLimit: negativeBalanceLimit,
        DisplayOrder: 1);

    // -- Name --

    [Fact]
    public void Create_EmptyName_ShouldFail()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(name: ""));
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Leave type name is required.");
    }

    [Fact]
    public void Create_NameTooLong_ShouldFail()
    {
        var result = _createValidator.TestValidate(
            MakeCreateCommand(name: new string('A', 101)));
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Leave type name cannot exceed 100 characters.");
    }

    [Fact]
    public void Create_ValidName_ShouldPass()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(name: "Annual Leave"));
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    // -- Color hex --

    [Theory]
    [InlineData("#4CAF50")]
    [InlineData("#ffffff")]
    [InlineData("#000000")]
    public void Create_ValidHexColor_ShouldPass(string color)
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(color: color));
        result.ShouldNotHaveValidationErrorFor(x => x.Color);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("4CAF50")]
    [InlineData("#4CAF5")]
    [InlineData("#4CAF500")]
    [InlineData("#GGGGGG")]
    public void Create_InvalidHexColor_ShouldFail(string color)
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(color: color));
        result.ShouldHaveValidationErrorFor(x => x.Color)
            .WithErrorMessage("Color must be a valid hex color code (e.g. #4CAF50).");
    }

    [Fact]
    public void Create_NullColor_ShouldPass()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(color: null));
        result.ShouldNotHaveValidationErrorFor(x => x.Color);
    }

    // -- Entitlement --

    [Fact]
    public void Create_NegativeEntitlement_ShouldFail()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(entitlement: -1));
        result.ShouldHaveValidationErrorFor(x => x.AnnualEntitlement)
            .WithErrorMessage("Annual entitlement must be zero or a positive number.");
    }

    [Fact]
    public void Create_ZeroEntitlement_ShouldPass()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(entitlement: 0));
        result.ShouldNotHaveValidationErrorFor(x => x.AnnualEntitlement);
    }

    [Fact]
    public void Create_PositiveEntitlement_ShouldPass()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(entitlement: 14));
        result.ShouldNotHaveValidationErrorFor(x => x.AnnualEntitlement);
    }

    // -- Accrual frequency --

    [Theory]
    [InlineData("Monthly")]
    [InlineData("Quarterly")]
    [InlineData("Yearly")]
    [InlineData("Upfront")]
    public void Create_ValidAccrualFrequency_ShouldPass(string frequency)
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(accrualFrequency: frequency));
        result.ShouldNotHaveValidationErrorFor(x => x.AccrualFrequency);
    }

    [Theory]
    [InlineData("Weekly")]
    [InlineData("Daily")]
    [InlineData("")]
    public void Create_InvalidAccrualFrequency_ShouldFail(string frequency)
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(accrualFrequency: frequency));
        result.ShouldHaveValidationErrorFor(x => x.AccrualFrequency);
    }

    // -- Gender --

    [Theory]
    [InlineData("All")]
    [InlineData("Male")]
    [InlineData("Female")]
    public void Create_ValidGender_ShouldPass(string gender)
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(gender: gender));
        result.ShouldNotHaveValidationErrorFor(x => x.Gender);
    }

    [Theory]
    [InlineData("Other")]
    [InlineData("NonBinary")]
    [InlineData("")]
    public void Create_InvalidGender_ShouldFail(string gender)
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(gender: gender));
        result.ShouldHaveValidationErrorFor(x => x.Gender);
    }

    // -- Day thresholds --

    [Fact]
    public void Create_ZeroDocumentDayThreshold_ShouldFail()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(documentDayThreshold: 0));
        result.ShouldHaveValidationErrorFor(x => x.DocumentDayThreshold)
            .WithErrorMessage("Document day threshold must be a positive number.");
    }

    [Fact]
    public void Create_NullDocumentDayThreshold_ShouldPass()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(documentDayThreshold: null));
        result.ShouldNotHaveValidationErrorFor(x => x.DocumentDayThreshold);
    }

    [Fact]
    public void Create_PositiveDocumentDayThreshold_ShouldPass()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(documentDayThreshold: 2));
        result.ShouldNotHaveValidationErrorFor(x => x.DocumentDayThreshold);
    }

    [Fact]
    public void Create_ZeroMaxConsecutiveDays_ShouldFail()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(maxConsecutiveDays: 0));
        result.ShouldHaveValidationErrorFor(x => x.MaxConsecutiveDays)
            .WithErrorMessage("Max consecutive days must be a positive number.");
    }

    [Fact]
    public void Create_ZeroMaxEncashDays_ShouldFail()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(maxEncashDays: 0));
        result.ShouldHaveValidationErrorFor(x => x.MaxEncashDays)
            .WithErrorMessage("Max encash days must be a positive number.");
    }

    [Fact]
    public void Create_ZeroNegativeBalanceLimit_ShouldFail()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(negativeBalanceLimit: 0));
        result.ShouldHaveValidationErrorFor(x => x.NegativeBalanceLimit)
            .WithErrorMessage("Negative balance limit must be a positive number.");
    }

    [Fact]
    public void Create_ZeroCarryForwardLimit_ShouldPass()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(carryForwardLimit: 0));
        result.ShouldNotHaveValidationErrorFor(x => x.CarryForwardLimit);
    }

    [Fact]
    public void Create_NegativeCarryForwardLimit_ShouldFail()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(carryForwardLimit: -1));
        result.ShouldHaveValidationErrorFor(x => x.CarryForwardLimit)
            .WithErrorMessage("Carry forward limit must be zero or a positive number.");
    }

    [Fact]
    public void Create_ZeroCarryForwardExpiryMonths_ShouldFail()
    {
        var result = _createValidator.TestValidate(MakeCreateCommand(carryForwardExpiryMonths: 0));
        result.ShouldHaveValidationErrorFor(x => x.CarryForwardExpiryMonths)
            .WithErrorMessage("Carry forward expiry months must be a positive number.");
    }

    // -- Update validator --

    [Fact]
    public void Update_EmptyId_ShouldFail()
    {
        var command = new UpdateLeaveTypeCommand(
            Guid.Empty, "Test", null, null, null, 10, "Upfront",
            null, null, false, false, null, false, null,
            false, false, "All", null, false, null);

        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.LeaveTypeId);
    }

    [Fact]
    public void Update_InvalidColor_ShouldFail()
    {
        var command = new UpdateLeaveTypeCommand(
            Guid.NewGuid(), "Test", null, "red", null, 10, "Upfront",
            null, null, false, false, null, false, null,
            false, false, "All", null, false, null);

        var result = _updateValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Color);
    }
}
