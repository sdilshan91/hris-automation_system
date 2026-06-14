using FluentValidation;
using HRM.Application.Features.Attendance.Commands;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// Shape-level validation for a clock-in request (US-ATT-001 §7). Policy-driven rules that need DB
/// access (geo-required, IP allowlist, geo-fence, photo-required, duplicate detection) are enforced
/// in the service layer; this validator only checks the request shape: coordinate ranges, that
/// latitude/longitude are supplied together, the photo URL length, and a valid source value.
/// </summary>
public sealed class ClockInValidator : AbstractValidator<ClockInCommand>
{
    private static readonly string[] s_allowedSources = ["WEB", "MOBILE_WEB"];

    public ClockInValidator()
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

        RuleFor(x => x.PhotoUrl)
            .MaximumLength(500).WithMessage("Photo URL must not exceed 500 characters.");

        RuleFor(x => x.Source)
            .Must(s => s is null || s_allowedSources.Contains(s))
            .WithMessage("Source must be either 'WEB' or 'MOBILE_WEB'.");
    }
}
