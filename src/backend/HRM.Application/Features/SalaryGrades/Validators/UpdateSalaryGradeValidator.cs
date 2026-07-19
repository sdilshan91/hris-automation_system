using FluentValidation;
using HRM.Application.Features.SalaryGrades.Commands;

namespace HRM.Application.Features.SalaryGrades.Validators;

public sealed class UpdateSalaryGradeValidator : AbstractValidator<UpdateSalaryGradeCommand>
{
    public UpdateSalaryGradeValidator()
    {
        RuleFor(x => x.SalaryGradeId)
            .NotEmpty().WithMessage("Salary grade id is required.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Salary grade code is required.")
            .MaximumLength(20).WithMessage("Salary grade code cannot exceed 20 characters.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Salary grade name is required.")
            .MaximumLength(100).WithMessage("Salary grade name cannot exceed 100 characters.");

        RuleFor(x => x.MinAmount)
            .GreaterThanOrEqualTo(0).WithMessage("Minimum amount must be zero or a positive number.");

        RuleFor(x => x.MaxAmount)
            .GreaterThanOrEqualTo(0).WithMessage("Maximum amount must be zero or a positive number.");

        RuleFor(x => x.MidAmount)
            .GreaterThanOrEqualTo(0).When(x => x.MidAmount.HasValue)
            .WithMessage("Midpoint amount must be zero or a positive number.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Matches("^[A-Za-z]{3}$").WithMessage("Currency must be a 3-letter ISO code (e.g. USD).");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");
    }
}
