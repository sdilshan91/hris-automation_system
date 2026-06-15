namespace HRM.Domain.Payroll;

/// <summary>
/// Stable string values for <c>PayrollSlip.PdfStatus</c> (US-PAY-004 FR-7). Stored as varchar(20). Kept as
/// constants (not an enum) so the persisted value is the literal the data spec (§7) names — Pending /
/// Generated / Failed — and so the FE can branch on the wire string without a converter.
/// </summary>
public static class PayslipPdfStatus
{
    /// <summary>Queued for generation but not yet rendered (the slip's initial PDF state).</summary>
    public const string Pending = "Pending";

    /// <summary>PDF was rendered and stored successfully (FR-7).</summary>
    public const string Generated = "Generated";

    /// <summary>Render or store failed; individually retryable (FR-8).</summary>
    public const string Failed = "Failed";
}
