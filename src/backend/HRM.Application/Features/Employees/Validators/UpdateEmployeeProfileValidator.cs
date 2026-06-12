using FluentValidation;
using HRM.Application.Features.Employees.Commands;
using HRM.Application.Features.Employees.DTOs;

namespace HRM.Application.Features.Employees.Validators;

public sealed class UpdateEmployeeProfileValidator : AbstractValidator<UpdateEmployeeProfileCommand>
{
    public UpdateEmployeeProfileValidator()
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty().WithMessage("Employee ID is required.");

        RuleFor(x => x.Request)
            .NotNull().WithMessage("Request body is required.");

        When(x => x.Request?.PersonalInfo is not null, () =>
        {
            RuleFor(x => x.Request.PersonalInfo!.FirstName)
                .MaximumLength(100).WithMessage("First name cannot exceed 100 characters.")
                .When(x => x.Request.PersonalInfo!.FirstName is not null);

            RuleFor(x => x.Request.PersonalInfo!.LastName)
                .MaximumLength(100).WithMessage("Last name cannot exceed 100 characters.")
                .When(x => x.Request.PersonalInfo!.LastName is not null);

            RuleFor(x => x.Request.PersonalInfo!.DateOfBirth)
                .Must(dob => dob!.Value < DateTime.UtcNow.Date)
                .WithMessage("Date of birth must be in the past.")
                .When(x => x.Request.PersonalInfo!.DateOfBirth.HasValue);
        });

        When(x => x.Request?.ContactInfo is not null, () =>
        {
            RuleFor(x => x.Request.ContactInfo!.Phone)
                .MaximumLength(20).WithMessage("Phone number cannot exceed 20 characters.")
                .When(x => x.Request.ContactInfo!.Phone is not null);

            RuleFor(x => x.Request.ContactInfo!.PersonalEmail)
                .MaximumLength(150).WithMessage("Personal email cannot exceed 150 characters.")
                .EmailAddress().WithMessage("Personal email must be a valid email address.")
                .When(x => x.Request.ContactInfo!.PersonalEmail is not null);

            RuleFor(x => x.Request.ContactInfo!.Address)
                .MaximumLength(500).WithMessage("Address cannot exceed 500 characters.")
                .When(x => x.Request.ContactInfo!.Address is not null);
        });

        When(x => x.Request?.EmploymentInfo is not null, () =>
        {
            RuleFor(x => x.Request.EmploymentInfo!.EmploymentType)
                .IsInEnum().WithMessage("Invalid employment type.")
                .When(x => x.Request.EmploymentInfo!.EmploymentType.HasValue);

            RuleFor(x => x.Request.EmploymentInfo!.Status)
                .IsInEnum().WithMessage("Invalid employee status.")
                .When(x => x.Request.EmploymentInfo!.Status.HasValue);
        });

        When(x => x.Request?.EmergencyContacts is not null, () =>
        {
            RuleForEach(x => x.Request.EmergencyContacts!).ChildRules(contact =>
            {
                contact.RuleFor(c => c.ContactName)
                    .NotEmpty().WithMessage("Emergency contact name is required.")
                    .MaximumLength(200).WithMessage("Contact name cannot exceed 200 characters.");

                contact.RuleFor(c => c.Relationship)
                    .NotEmpty().WithMessage("Relationship is required.")
                    .MaximumLength(50).WithMessage("Relationship cannot exceed 50 characters.");

                contact.RuleFor(c => c.Phone)
                    .NotEmpty().WithMessage("Emergency contact phone is required.")
                    .MaximumLength(20).WithMessage("Phone cannot exceed 20 characters.");

                contact.RuleFor(c => c.Email)
                    .MaximumLength(150).WithMessage("Email cannot exceed 150 characters.")
                    .EmailAddress().WithMessage("Email must be a valid email address.")
                    .When(c => c.Email is not null);
            });
        });
    }
}
