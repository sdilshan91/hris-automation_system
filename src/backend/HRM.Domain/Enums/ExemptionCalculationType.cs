namespace HRM.Domain.Enums;

/// <summary>
/// How a configurable income-tax exemption (<see cref="Entities.StatutoryExemption"/>) computes the amount it
/// removes from the tax base (TAX-2). Serialized as a string in the API (global JsonStringEnumConverter) and
/// stored as varchar via HasConversion&lt;string&gt;() so DB + wire values match the literals exactly.
/// </summary>
public enum ExemptionCalculationType
{
    /// <summary>A flat currency amount (the exemption's <c>Value</c> is money, per-period unless marked annual).</summary>
    FlatAmount,

    /// <summary>A percentage (0..100) of the employee's monthly gross earnings.</summary>
    PercentOfGross,

    /// <summary>A percentage (0..100) of a specific salary component's amount (requires a component id).</summary>
    PercentOfComponent,
}
