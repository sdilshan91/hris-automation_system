using FluentValidation;
using HRM.Application.Features.Attendance.Commands;

namespace HRM.Application.Features.Attendance.Validators;

public sealed class CreateShiftValidator : AbstractValidator<CreateShiftCommand>
{
    public CreateShiftValidator()
    {
        RuleFor(x => x.Request).NotNull();
        RuleFor(x => x.Request).SetValidator(new ShiftRequestValidator());
    }
}

public sealed class UpdateShiftValidator : AbstractValidator<UpdateShiftCommand>
{
    public UpdateShiftValidator()
    {
        RuleFor(x => x.ShiftId).NotEmpty();
        RuleFor(x => x.Request).NotNull();
        RuleFor(x => x.Request).SetValidator(new ShiftRequestValidator());
    }
}

public sealed class AssignShiftValidator : AbstractValidator<AssignShiftCommand>
{
    public AssignShiftValidator()
    {
        RuleFor(x => x.ShiftId).NotEmpty();

        RuleFor(x => x.EmployeeIds)
            .NotEmpty().WithMessage("At least one employee must be selected.")
            .Must(ids => ids.Count <= 500)
            .WithMessage("A single assignment can target at most 500 employees.");

        RuleFor(x => x.EmployeeIds)
            .Must(ids => ids.All(id => id != Guid.Empty))
            .WithMessage("Employee identifiers must be valid.");

        RuleFor(x => x.EffectiveFrom)
            .NotEqual(default(DateOnly)).WithMessage("An effective date is required.");
    }
}
