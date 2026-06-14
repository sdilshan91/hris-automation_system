// ============================================================================
// US-ATT-001: ClockInCommand validator unit tests.
// Shape-level checks only: coordinate ranges, lat/long pairing, photo URL length,
// and the allowed source values. Policy enforcement lives in the service.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Attendance.Commands;
using HRM.Application.Features.Attendance.Validators;

namespace HRM.Tests.Unit;

public sealed class ClockInValidatorTests
{
    private readonly ClockInValidator _validator = new();

    private static ClockInCommand Cmd(
        decimal? lat = null, decimal? lon = null,
        string? photoUrl = null, string? source = "WEB")
        => new(lat, lon, photoUrl, source, "10.0.0.1", "UnitTest/1.0", null);

    [Fact]
    public void Valid_NoCoordinates_Passes()
    {
        var result = _validator.TestValidate(Cmd());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Valid_WithCoordinates_Passes()
    {
        var result = _validator.TestValidate(Cmd(lat: 6.9271m, lon: 79.8612m));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void LatitudeOutOfRange_Fails()
    {
        var result = _validator.TestValidate(Cmd(lat: 91m, lon: 10m));
        result.ShouldHaveValidationErrorFor(x => x.Latitude);
    }

    [Fact]
    public void LongitudeOutOfRange_Fails()
    {
        var result = _validator.TestValidate(Cmd(lat: 10m, lon: 181m));
        result.ShouldHaveValidationErrorFor(x => x.Longitude);
    }

    [Fact]
    public void LatitudeWithoutLongitude_Fails()
    {
        var result = _validator.TestValidate(Cmd(lat: 10m));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void InvalidSource_Fails()
    {
        var result = _validator.TestValidate(Cmd(source: "DESKTOP_APP"));
        result.ShouldHaveValidationErrorFor(x => x.Source);
    }

    [Fact]
    public void MobileWebSource_Passes()
    {
        var result = _validator.TestValidate(Cmd(source: "MOBILE_WEB"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void PhotoUrlTooLong_Fails()
    {
        var result = _validator.TestValidate(Cmd(photoUrl: new string('a', 501)));
        result.ShouldHaveValidationErrorFor(x => x.PhotoUrl);
    }
}
