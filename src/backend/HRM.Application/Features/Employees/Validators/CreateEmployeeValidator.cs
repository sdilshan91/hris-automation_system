using FluentValidation;
using HRM.Application.Features.Employees.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Employees.Validators;

public sealed class CreateEmployeeValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MinimumLength(1).WithMessage("First name must be at least 1 character.")
            .MaximumLength(100).WithMessage("First name cannot exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MinimumLength(1).WithMessage("Last name must be at least 1 character.")
            .MaximumLength(100).WithMessage("Last name cannot exceed 100 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(150).WithMessage("Email cannot exceed 150 characters.")
            .EmailAddress().WithMessage("Email must be a valid email address.");

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Phone number cannot exceed 20 characters.")
            .When(x => x.Phone is not null);

        RuleFor(x => x.DateOfBirth)
            .Must(dob => dob!.Value < DateTime.UtcNow.Date)
            .WithMessage("Date of birth must be in the past.")
            .When(x => x.DateOfBirth.HasValue)
            .Must(dob => (DateTime.UtcNow.Date.Year - dob!.Value.Year -
                (DateTime.UtcNow.Date.DayOfYear < dob.Value.DayOfYear ? 1 : 0)) >= 16)
            .WithMessage("Employee must be at least 16 years old.")
            .When(x => x.DateOfBirth.HasValue);

        RuleFor(x => x.DateOfJoining)
            .NotEmpty().WithMessage("Date of joining is required.")
            .Must(doj => doj <= DateTime.UtcNow.Date.AddDays(90))
            .WithMessage("Date of joining cannot be more than 90 days in the future.");

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department is required.");

        RuleFor(x => x.JobTitleId)
            .NotEmpty().WithMessage("Job title is required.");

        RuleFor(x => x.EmploymentType)
            .IsInEnum().WithMessage("Invalid employment type. Valid values: FullTime, PartTime, Contract, Intern.");

        RuleFor(x => x.Status)
            .Must(s => s == null || s == EmployeeStatus.Active || s == EmployeeStatus.Probation)
            .WithMessage("Status on creation must be Active or Probation.")
            .When(x => x.Status.HasValue);
    }
}
