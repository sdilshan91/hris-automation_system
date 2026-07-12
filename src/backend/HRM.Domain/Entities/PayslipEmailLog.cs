namespace HRM.Domain.Entities;

/// <summary>
/// One per-employee record of a bulk payslip-email distribution (US-PAY-011 §7 / FR-5). The
/// <see cref="SendPayslipEmailsJob"/> writes one row per employee in a finalized run with status Queued ->
/// Sent / Failed / Skipped. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter +
/// <c>TenantInterceptor</c> (AC-5 — no cross-tenant leakage). Maps to the "payslip_email_log" table.
///
/// <para>NFR-3 idempotency/resumability: a re-run of the job skips rows already <c>Sent</c>; a selective
/// re-send (FR-4) re-attempts only the targeted / failed rows. The combination of (payroll_run_id,
/// employee_id) is the natural per-run-per-employee key — the latest row reflects the current delivery state.</para>
/// </summary>
public sealed class PayslipEmailLog : BaseEntity, IAuditExempt
{
    /// <summary>FK to the owning payroll run (§7, required).</summary>
    public Guid PayrollRunId { get; set; }

    /// <summary>FK to the payslip whose PDF is attached (§7, required).</summary>
    public Guid PayrollSlipId { get; set; }

    /// <summary>FK to the employee the email is addressed to (§7, required).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>The recipient email address resolved from Core HR at send time (§7, required). varchar(255).</summary>
    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary>
    /// Delivery status: one of <see cref="HRM.Domain.Payroll.EmailDeliveryStatus"/> (Queued, Sent, Failed,
    /// Skipped). Stored as varchar(20) — the persisted value == the wire string.
    /// </summary>
    public string Status { get; set; } = HRM.Domain.Payroll.EmailDeliveryStatus.Queued;

    /// <summary>When the email was accepted by the send seam (§7). Null until sent. timestamptz?.</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>The permanent-failure reason after retries (AC-4/FR-5), or the skip reason (AC-3). Null on success. text.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Number of send attempts beyond the first (Polly retries, AC-4/NFR-2). Default 0.</summary>
    public int RetryCount { get; set; }
}
