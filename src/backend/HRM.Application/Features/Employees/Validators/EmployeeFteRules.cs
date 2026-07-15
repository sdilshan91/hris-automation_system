using FluentValidation;

namespace HRM.Application.Features.Employees.Validators;

/// <summary>
/// US-CHR-013: the ONE definition of a valid FTE, shared by the create and profile-update validators.
///
/// <para>FTE multiplies leave entitlement and (opt-in) the overtime hourly base, so the bounds are
/// load-bearing rather than cosmetic: 0 or a negative would zero/invert an entitlement, and &gt; 1.00 would
/// silently over-accrue leave for everyone on the rule. The column is <c>numeric(3,2)</c>, so a 3 dp value
/// would be rounded by Postgres rather than rejected — the scale rule rejects it up front instead.</para>
/// </summary>
internal static class EmployeeFteRules
{
    public const string RangeMessage = "FTE must be greater than 0 and at most 1.00.";
    public const string ScaleMessage = "FTE cannot have more than 2 decimal places.";

    public static IRuleBuilderOptions<T, decimal?> ValidFte<T>(this IRuleBuilder<T, decimal?> rule)
        => rule
            .Must(f => f!.Value > 0m && f.Value <= 1.00m).WithMessage(RangeMessage)
            .Must(f => decimal.Round(f!.Value, 2) == f.Value).WithMessage(ScaleMessage);
}
