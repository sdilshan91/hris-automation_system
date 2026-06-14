using FluentValidation;
using HRM.Application.Features.Holidays.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Holidays.Validators;

public sealed class CreateHolidayValidator : AbstractValidator<CreateHolidayCommand>
{
    private static readonly HashSet<string> s_validTypes =
        new(Enum.GetNames<HolidayType>(), StringComparer.OrdinalIgnoreCase);

    public CreateHolidayValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Holiday name is required.")
            .MaximumLength(100).WithMessage("Holiday name cannot exceed 100 characters.");

        RuleFor(x => x.Date)
            .Must(d => d != default).WithMessage("Holiday date is required.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Holiday type is required.")
            .Must(BeValidType).WithMessage("Holiday type must be one of: Public, Restricted, Optional.");
    }

    private static bool BeValidType(string value) => s_validTypes.Contains(value);
}
