namespace HRM.Domain.Payroll;

/// <summary>
/// Stable string values for <c>PayslipEmailLog.Status</c> (US-PAY-011 §7 / FR-5). Stored as varchar(20). Kept
/// as constants (not an enum) so the persisted value is the literal the data spec names — Queued / Sent /
/// Failed / Skipped — and so the FE can branch on the wire string without a converter (same convention as
/// <see cref="PayslipPdfStatus"/> and <c>PayrollApprovalAction</c>).
/// </summary>
public static class EmailDeliveryStatus
{
    /// <summary>Row created, email not yet attempted (the initial state when the distribution job starts).</summary>
    public const string Queued = "Queued";

    /// <summary>Email was accepted by the send seam (SMTP acceptance, not inbox delivery — AC-2/§10).</summary>
    public const string Sent = "Sent";

    /// <summary>Send failed permanently after Polly retries; <c>FailureReason</c> carries the detail (AC-4/FR-5).</summary>
    public const string Failed = "Failed";

    /// <summary>Employee had no email address on file — skipped with a warning, job continues (AC-3/BR-3).</summary>
    public const string Skipped = "Skipped";

    /// <summary>All allowed values, for validation/aggregation.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Queued, Sent, Failed, Skipped };
}
