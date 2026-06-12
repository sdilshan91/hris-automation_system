using FluentValidation;
using HRM.Application.Features.Locations.Commands;

namespace HRM.Application.Features.Locations.Validators;

public sealed class CreateLocationValidator : AbstractValidator<CreateLocationCommand>
{
    /// <summary>
    /// Known IANA time zone IDs from TimeZoneInfo.
    /// </summary>
    private static readonly HashSet<string> s_validTimeZones =
        new(TimeZoneInfo.GetSystemTimeZones().Select(tz => tz.Id), StringComparer.OrdinalIgnoreCase);

    public CreateLocationValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Location name is required.")
            .MaximumLength(150).WithMessage("Location name cannot exceed 150 characters.");

        RuleFor(x => x.AddressLine1)
            .MaximumLength(250).WithMessage("Address line 1 cannot exceed 250 characters.");

        RuleFor(x => x.AddressLine2)
            .MaximumLength(250).WithMessage("Address line 2 cannot exceed 250 characters.");

        RuleFor(x => x.City)
            .MaximumLength(100).WithMessage("City cannot exceed 100 characters.");

        RuleFor(x => x.StateProvince)
            .MaximumLength(100).WithMessage("State/province cannot exceed 100 characters.");

        RuleFor(x => x.Country)
            .MaximumLength(100).WithMessage("Country cannot exceed 100 characters.");

        RuleFor(x => x.PostalCode)
            .MaximumLength(20).WithMessage("Postal code cannot exceed 20 characters.");

        RuleFor(x => x.TimeZone)
            .NotEmpty().WithMessage("Time zone is required.")
            .MaximumLength(50).WithMessage("Time zone cannot exceed 50 characters.")
            .Must(BeValidIanaTimeZone).WithMessage("Time zone must be a valid IANA time zone identifier (e.g. 'Asia/Colombo', 'America/New_York').");

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Phone cannot exceed 20 characters.");
    }

    private static bool BeValidIanaTimeZone(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
            return false;

        // Check both .NET TimeZoneInfo IDs and attempt IANA resolution
        if (s_validTimeZones.Contains(timeZone))
            return true;

        // On Windows, .NET may use Windows time zone IDs; try to find by IANA ID
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
    }
}
