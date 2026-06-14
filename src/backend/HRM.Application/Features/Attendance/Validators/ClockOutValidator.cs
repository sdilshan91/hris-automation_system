using FluentValidation;
using HRM.Application.Features.Attendance.Commands;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// Shape-level validation for a clock-out request (US-ATT-002 §7). Policy-driven rules that need DB
/// access (geo-required on clock-out, the open-record check, work-hours calculation) are enforced in
/// the service layer; this validator only checks the request shape: coordinate ranges and that
/// latitude/longitude are supplied together. Mirrors <see cref="ClockInValidator"/>.
/// </summary>
public sealed class ClockOutValidator : AbstractValidator<ClockOutCommand>
{
    public ClockOutValidator()
    {
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90m, 90m)
            .When(x => x.Latitude.HasValue)
            .WithMessage("Latitude must be between -90 and 90.");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180m, 180m)
            .When(x => x.Longitude.HasValue)
            .WithMessage("Longitude must be between -180 and 180.");

        // Coordinates must be supplied as a pair or not at all.
        RuleFor(x => x)
            .Must(x => x.Latitude.HasValue == x.Longitude.HasValue)
            .WithMessage("Latitude and longitude must be provided together.");
    }
}
