using FluentValidation;
using HRM.Application.Features.Locations.Commands;

namespace HRM.Application.Features.Locations.Validators;

public sealed class DeactivateLocationValidator : AbstractValidator<DeactivateLocationCommand>
{
    public DeactivateLocationValidator()
    {
        RuleFor(x => x.LocationId)
            .NotEmpty().WithMessage("Location ID is required.");
    }
}
