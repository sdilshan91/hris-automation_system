using FluentValidation;
using HRM.Application.Features.Auth.Commands;

namespace HRM.Application.Features.Auth.Validators;

/// <summary>
/// Validates MFA enrollment verification command.
/// Accepts either a 6-digit TOTP code or a recovery code (8-10 alphanumeric with optional dash).
/// </summary>
public sealed class MfaVerifyValidator : AbstractValidator<MfaVerifyCommand>
{
    public MfaVerifyValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required.")
            .Must(BeValidTotpOrRecoveryCode)
            .WithMessage("Code must be a 6-digit TOTP code or a valid recovery code (format: XXXXX-XXXXX).");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required.");
    }

    private static bool BeValidTotpOrRecoveryCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        // 6-digit TOTP code
        if (code.Length == 6 && code.All(char.IsDigit))
            return true;

        // Recovery code: 8-11 chars alphanumeric (with optional dash), e.g., XXXXX-XXXXX
        var stripped = code.Replace("-", "");
        return stripped.Length is >= 8 and <= 10 && stripped.All(char.IsLetterOrDigit);
    }
}
