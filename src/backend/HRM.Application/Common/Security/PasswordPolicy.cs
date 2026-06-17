namespace HRM.Application.Common.Security;

/// <summary>
/// A tenant's password policy (US-ADM-006 AC-4 / FR-5) as plain values, decoupled from the Tenant entity so
/// it can be validated as a pure, unit-testable function. Mirrors the typed Tenant password-policy columns.
/// </summary>
public sealed record PasswordPolicy(
    int MinLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigit,
    bool RequireSpecialCharacter,
    int HistoryCount,
    int? MaxAgeDays);

/// <summary>
/// Pure, dependency-free validation of a candidate password against a tenant <see cref="PasswordPolicy"/>.
/// This is the single enforcement point the settings update wires to (US-ADM-006 AC-4): after a Tenant Admin
/// sets MinLength = 12, a 10-char password fails here. The password change/reset flow can call
/// <see cref="Validate"/> with the current tenant's policy to enforce it.
/// </summary>
public static class PasswordPolicyValidator
{
    /// <summary>
    /// Validates <paramref name="password"/> against <paramref name="policy"/>. Returns the list of
    /// human-readable violations (empty ⇒ the password satisfies the policy).
    /// </summary>
    public static IReadOnlyList<string> Validate(string? password, PasswordPolicy policy)
    {
        var errors = new List<string>();
        var value = password ?? string.Empty;

        if (value.Length < policy.MinLength)
            errors.Add($"Password must be at least {policy.MinLength} characters.");

        if (policy.RequireUppercase && !value.Any(char.IsUpper))
            errors.Add("Password must contain at least one uppercase letter.");

        if (policy.RequireLowercase && !value.Any(char.IsLower))
            errors.Add("Password must contain at least one lowercase letter.");

        if (policy.RequireDigit && !value.Any(char.IsDigit))
            errors.Add("Password must contain at least one digit.");

        if (policy.RequireSpecialCharacter && !value.Any(c => !char.IsLetterOrDigit(c)))
            errors.Add("Password must contain at least one special character.");

        return errors;
    }

    /// <summary>True iff <paramref name="password"/> satisfies every rule in <paramref name="policy"/>.</summary>
    public static bool IsValid(string? password, PasswordPolicy policy) => Validate(password, policy).Count == 0;
}
