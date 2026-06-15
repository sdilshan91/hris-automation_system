using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A monthly payroll run for a tenant (US-PAY-003 FR-1). One row per (tenant, pay_month, pay_year)
/// attempt; only ONE non-cancelled run per period is permitted (BR-1, enforced by a partial unique index).
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>.
/// Maps to the "payroll_run" table.
///
/// <para>The run is created in <see cref="PayrollRunStatus.Queued"/> by the initiate command (AC-1) and
/// driven through its lifecycle (BR-6) by the Hangfire <c>ProcessPayrollRunJob</c>. The summary totals
/// (FR-8) and processed/skipped counters are stamped as the job runs and on completion.</para>
/// </summary>
public sealed class PayrollRun : BaseEntity
{
    /// <summary>Pay period month, 1-12 (FR-1, required).</summary>
    public int PayMonth { get; set; }

    /// <summary>Pay period year (FR-1, required).</summary>
    public int PayYear { get; set; }

    /// <summary>Current lifecycle status (FR-1/BR-6). Stored as varchar(20).</summary>
    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Queued;

    /// <summary>Total active employees considered in the run (FR-8, default 0).</summary>
    public int TotalEmployees { get; set; }

    /// <summary>Employees successfully processed into a slip (FR-6/FR-8 progress, default 0).</summary>
    public int ProcessedEmployees { get; set; }

    /// <summary>Employees skipped (e.g. no salary structure), AC-6/FR-8 (default 0).</summary>
    public int SkippedEmployees { get; set; }

    /// <summary>Sum of gross earnings across all slips (FR-8). numeric(18,2).</summary>
    public decimal TotalGross { get; set; }

    /// <summary>Sum of total deductions across all slips (FR-8). numeric(18,2).</summary>
    public decimal TotalDeductions { get; set; }

    /// <summary>Sum of net salary across all slips (FR-8). numeric(18,2).</summary>
    public decimal TotalNet { get; set; }

    /// <summary>Sum of statutory-component amounts across all slips (FR-8). numeric(18,2).</summary>
    public decimal TotalStatutory { get; set; }

    /// <summary>User who initiated the run (FR-1, required).</summary>
    public Guid InitiatedBy { get; set; }

    /// <summary>When the run was initiated (FR-1, required).</summary>
    public DateTime InitiatedAt { get; set; }

    /// <summary>When the job finished computing (status -&gt; ReviewPending), nullable.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>User who approved the run (BR-6), nullable.</summary>
    public Guid? ApprovedBy { get; set; }

    /// <summary>When the run was approved (BR-6), nullable.</summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>When the run was finalized (BR-7), nullable.</summary>
    public DateTime? FinalizedAt { get; set; }

    /// <summary>
    /// Idempotency key for the initiate request (FR-9), unique per tenant. Null when the request omitted
    /// the <c>Idempotency-Key</c> header.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Free-text run log capturing warnings such as skipped employees (AC-6) and processing notes (NFR-4).
    /// Newline-separated; appended to as the job runs.
    /// </summary>
    public string? RunLog { get; set; }
}
