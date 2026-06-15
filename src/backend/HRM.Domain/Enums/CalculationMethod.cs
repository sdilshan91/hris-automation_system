namespace HRM.Domain.Enums;

/// <summary>
/// How a salary component's value is derived during payroll calculation (US-PAY-001 FR-1/FR-4).
/// Serialized as a string in the API and stored as varchar via HasConversion&lt;string&gt;().
/// </summary>
public enum CalculationMethod
{
    /// <summary>A fixed monetary amount taken from <c>DefaultValue</c> (or the per-structure override).</summary>
    Fixed,

    /// <summary>A percentage (in <c>DefaultValue</c>) applied to the Basic component's value.</summary>
    PercentageOfBasic,

    /// <summary>A percentage (in <c>DefaultValue</c>) applied to total gross earnings.</summary>
    PercentageOfGross,

    /// <summary>A safe arithmetic expression in <c>FormulaExpression</c> (e.g. <c>basic * 0.12</c>).</summary>
    Formula,
}
