namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a monthly payroll run (US-PAY-003 FR-1/BR-6). Serialized as a string in the API
/// (global JsonStringEnumConverter) and stored as varchar(20) via HasConversion&lt;string&gt;().
///
/// <para>BR-6 transitions: Queued -&gt; Processing -&gt; ReviewPending -&gt; AwaitingApproval -&gt; Approved -&gt;
/// Finalized (US-PAY-008). Also AwaitingApproval -&gt; Rejected -&gt; ReviewPending (after corrections), and
/// AwaitingApproval -&gt; ReviewPending via ReturnToHR. Any pre-Finalized status may transition to Cancelled.
/// Finalized is immutable (BR-7).</para>
/// </summary>
public enum PayrollRunStatus
{
    /// <summary>Run record created; the Hangfire ProcessPayrollRunJob is enqueued but not yet started (AC-1).</summary>
    Queued,

    /// <summary>The Hangfire worker is computing slips for the period (AC-2).</summary>
    Processing,

    /// <summary>Computation complete; payslips persisted and awaiting HR review/approval (AC-3).</summary>
    ReviewPending,

    /// <summary>
    /// US-PAY-008 AC-1: HR submitted the run into the approval workflow; awaiting an approver decision.
    /// Reached from ReviewPending via SubmitForApproval; leaves to Approved (AC-2), Rejected (AC-3), or back
    /// to ReviewPending via ReturnToHR (FR-9).
    /// </summary>
    AwaitingApproval,

    /// <summary>HR has approved the run; awaiting finalization (BR-6).</summary>
    Approved,

    /// <summary>
    /// US-PAY-008 AC-3: an approver rejected the run with a reason. Terminal for this workflow instance;
    /// HR corrects + re-submits, which creates a NEW workflow instance and moves the run back to ReviewPending
    /// (BR-3).
    /// </summary>
    Rejected,

    /// <summary>Run is finalized and immutable; corrections go through a payroll adjustment (BR-7, US-PAY-007).</summary>
    Finalized,

    /// <summary>Run was cancelled before finalization (BR-6).</summary>
    Cancelled,

    /// <summary>
    /// Sentinel for a persisted <c>status</c> string outside this enum (ENH-021). Never written by the app —
    /// run status is only ever set by the code-controlled BR-6 transitions, never from user input — and only
    /// produced on READ by <see cref="Persistence.Converters.TolerantEnumToStringConverter{TEnum}"/> so one
    /// corrupt row does not 500 the payroll-run list/guard query. Appended so it does not disturb the existing
    /// ordinal values.
    /// </summary>
    Unknown = 99,
}
