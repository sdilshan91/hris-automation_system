namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a payroll adjustment (US-PAY-007 FR-4/FR-6). Serialized as a string in the API
/// (global JsonStringEnumConverter) and stored as varchar(20) via HasConversion&lt;string&gt;().
///
/// <para>Transitions: <see cref="Pending"/> -&gt; <see cref="Applied"/> when included in a payroll run that
/// persists slips (FR-4, prevents double application); <see cref="Pending"/> -&gt; <see cref="Cancelled"/>
/// when HR cancels a not-yet-applied adjustment (FR-6). An <see cref="Applied"/> adjustment is immutable.</para>
/// </summary>
public enum AdjustmentStatus
{
    /// <summary>Created and awaiting inclusion in a payroll run (FR-1). Only Pending adjustments are picked up.</summary>
    Pending,

    /// <summary>Included in a payroll run that persisted slips; stamped with <c>AppliedInPayrollRunId</c> (FR-4).</summary>
    Applied,

    /// <summary>Cancelled before application (FR-6); excluded from all future runs.</summary>
    Cancelled,
}
