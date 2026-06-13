using FluentValidation;
using HRM.Application.Features.LeaveTypes.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.LeaveTypes.Validators;

public sealed class CreateLeaveTypeValidator : AbstractValidator<CreateLeaveTypeCommand>
{
    private static readonly HashSet<string> s_validAccrualFrequencies =
        new(Enum.GetNames<AccrualFrequency>(), StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> s_validGenders =
        new(Enum.GetNames<LeaveTypeGender>(), StringComparer.OrdinalIgnoreCase);

    public CreateLeaveTypeValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Leave type name is required.")
            .MaximumLength(100).WithMessage("Leave type name cannot exceed 100 characters.");

        RuleFor(x => x.Code)
            .MaximumLength(20).WithMessage("Code cannot exceed 20 characters.");

        RuleFor(x => x.Color)
            .MaximumLength(7).WithMessage("Color cannot exceed 7 characters.")
            .Matches(@"^#[0-9A-Fa-f]{6}$").When(x => !string.IsNullOrEmpty(x.Color))
            .WithMessage("Color must be a valid hex color code (e.g. #4CAF50).");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");

        RuleFor(x => x.AnnualEntitlement)
            .GreaterThanOrEqualTo(0).WithMessage("Annual entitlement must be zero or a positive number.");

        RuleFor(x => x.AccrualFrequency)
            .NotEmpty().WithMessage("Accrual frequency is required.")
            .Must(BeValidAccrualFrequency).WithMessage("Accrual frequency must be one of: Monthly, Quarterly, Yearly, Upfront.");

        RuleFor(x => x.CarryForwardLimit)
            .GreaterThanOrEqualTo(0).When(x => x.CarryForwardLimit.HasValue)
            .WithMessage("Carry forward limit must be zero or a positive number.");

        RuleFor(x => x.CarryForwardExpiryMonths)
            .GreaterThan(0).When(x => x.CarryForwardExpiryMonths.HasValue)
            .WithMessage("Carry forward expiry months must be a positive number.");

        RuleFor(x => x.DocumentDayThreshold)
            .GreaterThan(0).When(x => x.DocumentDayThreshold.HasValue)
            .WithMessage("Document day threshold must be a positive number.");

        RuleFor(x => x.MaxEncashDays)
            .GreaterThan(0).When(x => x.MaxEncashDays.HasValue)
            .WithMessage("Max encash days must be a positive number.");

        RuleFor(x => x.Gender)
            .NotEmpty().WithMessage("Gender is required.")
            .Must(BeValidGender).WithMessage("Gender must be one of: All, Male, Female.");

        RuleFor(x => x.MaxConsecutiveDays)
            .GreaterThan(0).When(x => x.MaxConsecutiveDays.HasValue)
            .WithMessage("Max consecutive days must be a positive number.");

        RuleFor(x => x.NegativeBalanceLimit)
            .GreaterThan(0).When(x => x.NegativeBalanceLimit.HasValue)
            .WithMessage("Negative balance limit must be a positive number.");
    }

    private static bool BeValidAccrualFrequency(string value)
        => s_validAccrualFrequencies.Contains(value);

    private static bool BeValidGender(string value)
        => s_validGenders.Contains(value);
}
