using HRM.Domain.Enums;

namespace HRM.Domain.Payroll;

/// <summary>
/// One component in a structure, with the rule needed to resolve its value from a CTC (US-PAY-002 FR-2).
/// The rule is the component's effective method/value (per-structure overrides already merged in).
/// </summary>
/// <param name="ComponentId">Salary component id.</param>
/// <param name="Code">Component code — the formula variable name (case-insensitive).</param>
/// <param name="Type">Component classification (only <see cref="SalaryComponentType.Earning"/> sums into CTC).</param>
/// <param name="Method">Effective calculation method.</param>
/// <param name="Value">Effective value: a fixed annual amount (Fixed) or a percentage (PercentageOf*). Null for Formula.</param>
/// <param name="Formula">Effective formula expression (Formula only).</param>
/// <param name="ProcessingOrder">Resolution order (lower first); earlier components are visible to later formulas.</param>
public readonly record struct CtcComponentRule(
    Guid ComponentId,
    string Code,
    SalaryComponentType Type,
    CalculationMethod Method,
    decimal? Value,
    string? Formula,
    int ProcessingOrder);

/// <summary>A resolved component line in a CTC breakdown (US-PAY-002 FR-3).</summary>
/// <param name="ComponentId">Salary component id.</param>
/// <param name="Code">Component code.</param>
/// <param name="Type">Component classification.</param>
/// <param name="AnnualAmount">Resolved annual amount (rounded to 2 dp).</param>
/// <param name="MonthlyAmount">AnnualAmount / 12 (rounded to 2 dp).</param>
/// <param name="IsOverride">True when an explicit per-employee override supplied this value.</param>
public readonly record struct CtcComponentResult(
    Guid ComponentId,
    string Code,
    SalaryComponentType Type,
    decimal AnnualAmount,
    decimal MonthlyAmount,
    bool IsOverride);

/// <summary>
/// Resolves a salary structure's component rules into concrete annual + monthly amounts for a declared
/// annual CTC (US-PAY-002 FR-2/FR-3). Pure + deterministic — no DB / tenant access — so it is fully unit
/// testable and reused by both the preview query and the assignment command.
///
/// <para>Resolution semantics (in ProcessingOrder):</para>
/// <list type="bullet">
///   <item><c>Fixed</c> — the effective value is taken as a fixed ANNUAL amount.</item>
///   <item><c>PercentageOfBasic</c> — value% of the already-resolved component whose code is "BASIC".</item>
///   <item><c>PercentageOfGross</c> — value% of the declared annual CTC (gross == CTC at assignment time).</item>
///   <item><c>Formula</c> — evaluated via <see cref="SalaryFormula"/> with variables = every earlier
///         component's resolved ANNUAL amount (by code) plus <c>basic</c>, <c>gross</c>, and <c>ctc</c>
///         (all == the declared annual CTC for gross/ctc; basic == the resolved BASIC annual amount).</item>
/// </list>
///
/// <para>A per-employee override (by component id) short-circuits the rule: the supplied annual amount is
/// used verbatim and the line is flagged <see cref="CtcComponentResult.IsOverride"/> (AC-3).</para>
///
/// All money is rounded to 2 decimal places (MidpointRounding.AwayFromZero) to match numeric(18,2).
/// </summary>
public static class CtcBreakdownCalculator
{
    private const string BasicCode = "BASIC";

    /// <summary>
    /// Resolves the breakdown. <paramref name="overrides"/> maps component id → fixed annual amount that
    /// replaces the calculated value (AC-3). Components are returned in ProcessingOrder.
    /// </summary>
    /// <exception cref="SalaryFormula.FormulaException">A formula references an unknown variable or is malformed.</exception>
    public static IReadOnlyList<CtcComponentResult> Calculate(
        IReadOnlyList<CtcComponentRule> rules,
        decimal annualCtc,
        IReadOnlyDictionary<Guid, decimal>? overrides = null)
    {
        var ordered = rules.OrderBy(r => r.ProcessingOrder).ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase).ToList();

        // Running map of resolved annual amounts keyed by component code (formula variable namespace).
        var resolved = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var results = new List<CtcComponentResult>(ordered.Count);

        foreach (var rule in ordered)
        {
            decimal annual;
            var isOverride = false;

            if (overrides is not null && overrides.TryGetValue(rule.ComponentId, out var overrideAmount))
            {
                annual = overrideAmount;
                isOverride = true;
            }
            else
            {
                annual = ResolveRule(rule, annualCtc, resolved);
            }

            annual = Round(annual);
            resolved[rule.Code] = annual;

            results.Add(new CtcComponentResult(
                rule.ComponentId, rule.Code, rule.Type, annual, Round(annual / 12m), isOverride));
        }

        return results;
    }

    private static decimal ResolveRule(CtcComponentRule rule, decimal annualCtc, Dictionary<string, decimal> resolved)
    {
        switch (rule.Method)
        {
            case CalculationMethod.Fixed:
                return rule.Value ?? 0m;

            case CalculationMethod.PercentageOfGross:
                return annualCtc * (rule.Value ?? 0m) / 100m;

            case CalculationMethod.PercentageOfBasic:
                var basic = resolved.TryGetValue(BasicCode, out var b) ? b : 0m;
                return basic * (rule.Value ?? 0m) / 100m;

            case CalculationMethod.Formula:
                if (string.IsNullOrWhiteSpace(rule.Formula))
                    return 0m;
                var variables = BuildFormulaVariables(annualCtc, resolved);
                return SalaryFormula.Evaluate(rule.Formula, variables);

            default:
                return 0m;
        }
    }

    private static Dictionary<string, decimal> BuildFormulaVariables(decimal annualCtc, Dictionary<string, decimal> resolved)
    {
        var variables = new Dictionary<string, decimal>(resolved, StringComparer.OrdinalIgnoreCase)
        {
            ["gross"] = annualCtc,
            ["ctc"] = annualCtc,
        };
        // "basic" alias points at the resolved BASIC component (0 if not yet resolved).
        variables["basic"] = resolved.TryGetValue(BasicCode, out var basic) ? basic : 0m;
        return variables;
    }

    /// <summary>Total of all EARNING annual amounts in a breakdown (the figure compared to declared CTC for FR-6).</summary>
    public static decimal TotalEarnings(IReadOnlyList<CtcComponentResult> results)
        => results.Where(r => r.Type == SalaryComponentType.Earning).Sum(r => r.AnnualAmount);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
