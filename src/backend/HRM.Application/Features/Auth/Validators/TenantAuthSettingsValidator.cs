using FluentValidation;
using HRM.Application.Features.Auth.Commands;

namespace HRM.Application.Features.Auth.Validators;

/// <summary>
/// Validates tenant auth settings update command.
/// MfaPolicy must be one of: off, optional, required.
/// MfaRequiredRoles must have non-empty role names when policy is "required".
/// </summary>
public sealed class TenantAuthSettingsValidator : AbstractValidator<UpdateTenantAuthSettingsCommand>
{
    private static readonly string[] ValidPolicies = ["off", "optional", "required"];

    public TenantAuthSettingsValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("Tenant ID is required.");

        RuleFor(x => x.Request)
            .NotNull().WithMessage("Request body is required.");

        RuleFor(x => x.Request.MfaPolicy)
            .NotEmpty().WithMessage("MFA policy is required.")
            .Must(p => ValidPolicies.Contains(p))
            .WithMessage("MFA policy must be one of: off, optional, required.");

        When(x => x.Request.MfaPolicy == "required", () =>
        {
            RuleFor(x => x.Request.MfaRequiredRoles)
                .NotEmpty().WithMessage("At least one role must be specified when MFA policy is 'required'.")
                .ForEach(role =>
                {
                    role.NotEmpty().WithMessage("Role name cannot be empty.");
                });
        });
    }
}
