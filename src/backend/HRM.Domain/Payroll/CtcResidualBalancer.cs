using HRM.Domain.Enums;

namespace HRM.Domain.Payroll;

/// <summary>
/// The outcome of a residual-balancing pass (BUG-070). Either the balanced component list (total earnings ==
/// target CTC within tolerance) or a rejection with a machine code + message.
/// </summary>
public readonly record struct CtcBalanceResult(
    bool Success,
    IReadOnlyList<CtcComponentResult> Components,
    string? ErrorCode,
    string? Error)
{
    public static CtcBalanceResult Ok(IReadOnlyList<CtcComponentResult> components) =>
        new(true, components, null, null);

    public static CtcBalanceResult Fail(string errorCode, string error) =>
        new(false, Array.Empty<CtcComponentResult>(), errorCode, error);
}

/// <summary>
/// RESIDUAL-TO-ONE-COMPONENT CTC balancer (BUG-070). Pure + deterministic — no DB, no tenant access — so the
/// balancing math is unit-testable directly.
///
/// <para>Problem: when a caller overrides a subset of salary components, the sum of earnings no longer equals
/// the declared CTC, and the assignment is rejected (<c>ctc_sum_mismatch</c>) unless every untouched component
/// is re-supplied. This soaks the leftover (target CTC − sum of all other earnings) into ONE designated
/// residual earning component, so a partial override balances without re-supplying the rest.</para>
///
/// <para>Residual selection (in order): a "special allowance" earning (code containing SPECIAL, or SPL/SA),
/// else the BASIC earning. The designated residual is the one that absorbs the delta.</para>
///
/// <para>Guard rails:</para>
/// <list type="bullet">
///   <item>Absorbing the delta must not drive the residual below <paramref name="residualFloor"/> (default 0):
///   <c>residual_would_be_negative</c>.</item>
///   <item>The residual component itself must not be one of the overridden components — you cannot both fix and
///   balance the same component: <c>residual_component_overridden</c>.</item>
///   <item>No designated residual earning exists to absorb the delta: <c>no_residual_component</c>.</item>
/// </list>
///
/// <para>When the components already balance within <paramref name="tolerance"/> (e.g. a formula-driven residual
/// structure that self-absorbs), this is a no-op and returns the input unchanged.</para>
/// </summary>
public static class CtcResidualBalancer
{
    private const string BasicCode = "BASIC";

    /// <summary>
    /// Balances the residual earning component so total earnings equal <paramref name="annualCtc"/>.
    /// </summary>
    /// <param name="resolved">Components with all overrides already applied verbatim (from <see cref="CtcBreakdownCalculator"/>).</param>
    /// <param name="annualCtc">The declared target annual CTC (= total of earning components).</param>
    /// <param name="overriddenComponentIds">Component ids supplied as explicit overrides in this request.</param>
    /// <param name="tolerance">Max |total earnings − CTC| treated as "already balanced" (default ±1, matches FR-6).</param>
    /// <param name="residualFloor">Lowest the residual may be driven to (default 0 — no negative component).</param>
    public static CtcBalanceResult Balance(
        IReadOnlyList<CtcComponentResult> resolved,
        decimal annualCtc,
        IReadOnlySet<Guid> overriddenComponentIds,
        decimal tolerance = 1m,
        decimal residualFloor = 0m)
    {
        var totalEarnings = CtcBreakdownCalculator.TotalEarnings(resolved);
        var delta = Round(annualCtc - totalEarnings);

        // Already balanced (e.g. a formula-driven residual absorbed the override): nothing to do.
        if (Math.Abs(delta) <= tolerance)
            return CtcBalanceResult.Ok(resolved);

        var residualIndex = SelectResidualIndex(resolved);
        if (residualIndex < 0)
            return CtcBalanceResult.Fail(
                "no_residual_component",
                "No residual earning component (Special Allowance or Basic) is available to absorb the CTC difference. " +
                "Re-supply the affected components or add a Special Allowance / Basic component to the structure.");

        var residual = resolved[residualIndex];

        if (overriddenComponentIds.Contains(residual.ComponentId))
            return CtcBalanceResult.Fail(
                "residual_component_overridden",
                $"The residual component '{residual.Code}' cannot be both overridden and used to balance the CTC. " +
                "Override a different component, or omit the residual override.");

        var newAmount = Round(residual.AnnualAmount + delta);
        if (newAmount < residualFloor)
            return CtcBalanceResult.Fail(
                "residual_would_be_negative",
                $"Balancing the CTC would drive the residual component '{residual.Code}' to {newAmount:0.##}, " +
                $"below the minimum of {residualFloor:0.##}. Reduce the overridden amounts or increase the CTC.");

        var balanced = resolved.ToArray();
        balanced[residualIndex] = residual with
        {
            AnnualAmount = newAmount,
            MonthlyAmount = Round(newAmount / 12m),
        };

        return CtcBalanceResult.Ok(balanced);
    }

    /// <summary>
    /// Picks the residual earning component: a "special allowance" earning first, else the BASIC earning.
    /// Returns the index into <paramref name="resolved"/>, or -1 when neither exists.
    /// </summary>
    private static int SelectResidualIndex(IReadOnlyList<CtcComponentResult> resolved)
    {
        var special = -1;
        var basic = -1;
        for (var i = 0; i < resolved.Count; i++)
        {
            var c = resolved[i];
            if (c.Type != SalaryComponentType.Earning)
                continue;
            if (special < 0 && IsSpecialAllowanceCode(c.Code))
                special = i;
            else if (basic < 0 && string.Equals(c.Code, BasicCode, StringComparison.OrdinalIgnoreCase))
                basic = i;
        }

        return special >= 0 ? special : basic;
    }

    /// <summary>True for codes that designate a "special allowance" residual (SPECIAL*, SPL, SA).</summary>
    private static bool IsSpecialAllowanceCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var c = code.Trim();
        return c.Contains("SPECIAL", StringComparison.OrdinalIgnoreCase)
            || c.Equals("SPL", StringComparison.OrdinalIgnoreCase)
            || c.Equals("SA", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
