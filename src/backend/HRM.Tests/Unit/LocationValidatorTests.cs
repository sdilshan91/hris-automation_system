// ============================================================================
// US-CHR-007: Location Validator Unit Tests
// Tests FluentValidation rules for create/update commands:
// name required/length, time zone required + IANA format check.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Locations.Commands;
using HRM.Application.Features.Locations.Validators;

namespace HRM.Tests.Unit;

public sealed class LocationValidatorTests
{
    private readonly CreateLocationValidator _createValidator = new();
    private readonly UpdateLocationValidator _updateValidator = new();

    // -- Create: Name validation --

    [Fact]
    public void Create_EmptyName_ShouldFail()
    {
        var command = new CreateLocationCommand(
            "", null, null, null, null, null, null, "UTC", null);

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Location name is required.");
    }

    [Fact]
    public void Create_NameTooLong_ShouldFail()
    {
        var command = new CreateLocationCommand(
            new string('A', 151), null, null, null, null, null, null, "UTC", null);

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Location name cannot exceed 150 characters.");
    }

    [Fact]
    public void Create_ValidName_ShouldPass()
    {
        var command = new CreateLocationCommand(
            "Head Office", null, null, null, null, null, null, "UTC", null);

        var result = _createValidator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    // -- Create: TimeZone validation --

    [Fact]
    public void Create_EmptyTimeZone_ShouldFail()
    {
        var command = new CreateLocationCommand(
            "Office", null, null, null, null, null, null, "", null);

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.TimeZone);
    }

    [Fact]
    public void Create_InvalidTimeZone_ShouldFail()
    {
        var command = new CreateLocationCommand(
            "Office", null, null, null, null, null, null, "Invalid/Zone", null);

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.TimeZone)
            .WithErrorMessage("Time zone must be a valid IANA time zone identifier (e.g. 'Asia/Colombo', 'America/New_York').");
    }

    [Fact]
    public void Create_ValidIanaTimeZone_ShouldPass()
    {
        var command = new CreateLocationCommand(
            "Office", null, null, null, null, null, null, "America/New_York", null);

        var result = _createValidator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.TimeZone);
    }

    [Fact]
    public void Create_UtcTimeZone_ShouldPass()
    {
        var command = new CreateLocationCommand(
            "Office", null, null, null, null, null, null, "UTC", null);

        var result = _createValidator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.TimeZone);
    }

    // -- Create: Address field length validation --

    [Fact]
    public void Create_AddressLine1TooLong_ShouldFail()
    {
        var command = new CreateLocationCommand(
            "Office", new string('A', 251), null, null, null, null, null, "UTC", null);

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.AddressLine1);
    }

    [Fact]
    public void Create_PhoneTooLong_ShouldFail()
    {
        var command = new CreateLocationCommand(
            "Office", null, null, null, null, null, null, "UTC",
            new string('1', 21));

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Phone);
    }

    // -- Update: LocationId validation --

    [Fact]
    public void Update_EmptyLocationId_ShouldFail()
    {
        var command = new UpdateLocationCommand(
            Guid.Empty, "Office", null, null, null, null, null, null, "UTC", null);

        var result = _updateValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.LocationId);
    }

    [Fact]
    public void Update_InvalidTimeZone_ShouldFail()
    {
        var command = new UpdateLocationCommand(
            Guid.NewGuid(), "Office", null, null, null, null, null, null, "Fake/Zone", null);

        var result = _updateValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.TimeZone);
    }

    [Fact]
    public void Update_ValidCommand_ShouldPass()
    {
        var command = new UpdateLocationCommand(
            Guid.NewGuid(), "Office", "123 St", null, "City", "State", "Country", "12345",
            "America/Chicago", "+1234567890");

        var result = _updateValidator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
