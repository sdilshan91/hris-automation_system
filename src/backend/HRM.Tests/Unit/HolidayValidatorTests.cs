// ============================================================================
// US-LV-007 FR-1 / AC-1: Holiday create/update validator tests.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Holidays.Commands;
using HRM.Application.Features.Holidays.Validators;

namespace HRM.Tests.Unit;

public sealed class HolidayValidatorTests
{
    private readonly CreateHolidayValidator _createValidator = new();
    private readonly UpdateHolidayValidator _updateValidator = new();

    private static CreateHolidayCommand CreateCmd(
        string name = "New Year's Day", string type = "Public")
        => new(name, new DateOnly(2026, 1, 1), type, null, null, false);

    private static UpdateHolidayCommand UpdateCmd(
        string name = "New Year's Day", string type = "Public")
        => new(Guid.NewGuid(), name, new DateOnly(2026, 1, 1), type, null, null, false);

    [Fact]
    public void Create_Valid_Passes()
        => _createValidator.TestValidate(CreateCmd()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Create_EmptyName_Fails()
        => _createValidator.TestValidate(CreateCmd(name: ""))
            .ShouldHaveValidationErrorFor(x => x.Name);

    [Fact]
    public void Create_NameTooLong_Fails()
        => _createValidator.TestValidate(CreateCmd(name: new string('x', 101)))
            .ShouldHaveValidationErrorFor(x => x.Name);

    [Theory]
    [InlineData("Public")]
    [InlineData("restricted")]
    [InlineData("OPTIONAL")]
    public void Create_ValidType_CaseInsensitive_Passes(string type)
        => _createValidator.TestValidate(CreateCmd(type: type))
            .ShouldNotHaveValidationErrorFor(x => x.Type);

    [Fact]
    public void Create_InvalidType_Fails()
        => _createValidator.TestValidate(CreateCmd(type: "Bogus"))
            .ShouldHaveValidationErrorFor(x => x.Type);

    [Fact]
    public void Create_DefaultDate_Fails()
    {
        var cmd = new CreateHolidayCommand("Name", default, "Public", null, null, false);
        _createValidator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Date);
    }

    [Fact]
    public void Update_Valid_Passes()
        => _updateValidator.TestValidate(UpdateCmd()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Update_InvalidType_Fails()
        => _updateValidator.TestValidate(UpdateCmd(type: "Nope"))
            .ShouldHaveValidationErrorFor(x => x.Type);
}
