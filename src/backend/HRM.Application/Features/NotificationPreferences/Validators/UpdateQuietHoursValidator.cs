using FluentValidation;
using HRM.Application.Features.NotificationPreferences.Commands;

namespace HRM.Application.Features.NotificationPreferences.Validators;

/// <summary>
/// US-NTF-003 FR-9: structural validation of the quiet-hours update. When quiet hours are enabled, start,
/// end and timezone are required and start must differ from end. The IANA timezone is RESOLVED in the
/// service (authoritative <see cref="System.TimeZoneInfo"/> lookup) so the message is accurate per platform.
/// </summary>
public sealed class UpdateQuietHoursValidator : AbstractValidator<UpdateQuietHoursCommand>
{
    public UpdateQuietHoursValidator()
    {
        When(x => x.Enabled, () =>
        {
            RuleFor(x => x.Start)
                .NotNull().WithMessage("Quiet hours start time is required when quiet hours are enabled.");

            RuleFor(x => x.End)
                .NotNull().WithMessage("Quiet hours end time is required when quiet hours are enabled.");

            RuleFor(x => x.Timezone)
                .NotEmpty().WithMessage("A timezone is required when quiet hours are enabled.");

            RuleFor(x => x)
                .Must(x => x.Start != x.End)
                .WithMessage("Quiet hours start and end times must differ.")
                .When(x => x.Start.HasValue && x.End.HasValue);
        });
    }
}
