namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a monthly payroll run (US-PAY-003 FR-1/BR-6). Serialized as a string in the API
/// (global JsonStringEnumConverter) and stored as varchar(20) via HasConversion&lt;string&gt;().
///
/// <para>BR-6 transitions: Queued -&gt; Processing -&gt; ReviewPending -&gt; Approved -&gt; Finalized.
/// Any pre-Finalized status may transition to Cancelled. Finalized is immutable (BR-7).</para>
/// </summary>
public enum PayrollRunStatus
{
    /// <summary>Run record created; the Hangfire ProcessPayrollRunJob is enqueued but not yet started (AC-1).</summary>
    Queued,

    /// <summary>The Hangfire worker is computing slips for the period (AC-2).</summary>
    Processing,

    /// <summary>Computation complete; payslips persisted and awaiting HR review/approval (AC-3).</summary>
    ReviewPending,

    /// <summary>HR has approved the run; awaiting finalization (BR-6).</summary>
    Approved,

    /// <summary>Run is finalized and immutable; corrections go through a payroll adjustment (BR-7, US-PAY-007).</summary>
    Finalized,

    /// <summary>Run was cancelled before finalization (BR-6).</summary>
    Cancelled,
}
