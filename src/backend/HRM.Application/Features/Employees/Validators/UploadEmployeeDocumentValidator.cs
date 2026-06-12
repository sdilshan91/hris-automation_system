using FluentValidation;
using HRM.Application.Features.Employees.Commands;

namespace HRM.Application.Features.Employees.Validators;

/// <summary>
/// Validates the upload document command metadata (US-CHR-008 FR-1, FR-2, AC-3).
/// File content validation (MIME, size, virus) is handled at the service level.
/// </summary>
public sealed class UploadEmployeeDocumentValidator : AbstractValidator<UploadEmployeeDocumentCommand>
{
    private static readonly string[] ValidCategories = { "Contract", "ID", "Certificate", "Other" };

    public UploadEmployeeDocumentValidator()
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty().WithMessage("Employee ID is required.");

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("File name is required.")
            .MaximumLength(255).WithMessage("File name must not exceed 255 characters.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Document category is required.")
            .Must(c => ValidCategories.Contains(c))
            .WithMessage("Invalid category. Allowed: Contract, ID, Certificate, Other.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.");

        RuleFor(x => x.ExpiryDate)
            .GreaterThan(DateTime.UtcNow.Date)
            .When(x => x.ExpiryDate.HasValue)
            .WithMessage("Expiry date must be in the future.");
    }
}
