namespace HRM.Domain.Entities;

/// <summary>
/// One employee's computed payslip within a payroll run (US-PAY-003 FR-5f). Holds the rolled-up gross /
/// total-deductions / net plus the attendance basis (working/paid days, LOP). The per-component breakdown
/// lives in <see cref="PayrollSlipDetail"/>. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF
/// global query filter + <c>TenantInterceptor</c>. Maps to the "payroll_slip" table.
///
/// <para>Composite index on (tenant_id, payroll_run_id, employee_id) per the data spec (§19.12) backs the
/// "slips for a run" read and the re-run replacement (FR-7).</para>
/// </summary>
public sealed class PayrollSlip : BaseEntity
{
    /// <summary>FK to the owning payroll run (FR-5, required).</summary>
    public Guid PayrollRunId { get; set; }

    /// <summary>FK to the employee this slip belongs to (FR-5, required).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Total earning + reimbursement amounts (gross pay before deductions) (FR-5e). numeric(18,2).</summary>
    public decimal GrossEarnings { get; set; }

    /// <summary>Total of all deduction + statutory + LOP amounts (FR-5e). numeric(18,2).</summary>
    public decimal TotalDeductions { get; set; }

    /// <summary>Net salary = gross - total deductions (FR-5e). numeric(18,2). Reconciled to the detail sum (BR-8).</summary>
    public decimal NetSalary { get; set; }

    /// <summary>Loss-of-pay days applied (BR-2). numeric(5,2), default 0.</summary>
    public decimal LopDays { get; set; }

    /// <summary>Scheduled working days in the period (BR-2 denominator). numeric(5,2), required.</summary>
    public decimal WorkingDays { get; set; }

    /// <summary>Paid days = working days - LOP days, pro-rated for mid-month join/separation (BR-4/BR-5). numeric(5,2), required.</summary>
    public decimal PaidDays { get; set; }

    /// <summary>Pay period month, 1-12 (denormalized for history, required).</summary>
    public int PayMonth { get; set; }

    /// <summary>Pay period year (denormalized for history, required).</summary>
    public int PayYear { get; set; }

    /// <summary>The per-component breakdown lines (FR-5f). Owned collection; loaded explicitly where needed.</summary>
    public List<PayrollSlipDetail> Details { get; set; } = [];

    // ── US-PAY-004: PDF payslip generation (FR-7) ──────────────────────────────

    /// <summary>
    /// When this slip's PDF was last successfully rendered + stored (US-PAY-004 FR-7). Null until generated;
    /// updated (overwritten) on regenerate (AC-5). timestamptz?.
    /// </summary>
    public DateTime? PdfGeneratedAt { get; set; }

    /// <summary>
    /// Tenant-scoped relative storage path of the rendered PDF (US-PAY-004 FR-5/FR-7):
    /// <c>{tenantId}/payroll/{runId}/{employeeId}.pdf</c> (the path WITHIN the tenant scope is
    /// <c>payroll/{runId}/{employeeId}.pdf</c>). Null until generated. varchar(500)?.
    /// </summary>
    public string? PdfStoragePath { get; set; }

    /// <summary>
    /// PDF generation status (US-PAY-004 FR-7): Pending (queued, not yet rendered) / Generated (rendered +
    /// stored) / Failed (render or store error, individually retryable per FR-8). varchar(20)?.
    /// </summary>
    public string? PdfStatus { get; set; }

    /// <summary>Size of the rendered PDF in bytes (US-PAY-004 FR-7/NFR-2 &lt;=200KB target). Null until generated. int?.</summary>
    public int? PdfFileSizeBytes { get; set; }

    // ── US-PAY-010: attendance + leave integration enrichment (§7) ─────────────

    /// <summary>
    /// Total approved overtime hours rolled into this slip (US-PAY-010 AC-2/FR-4), from the attendance pull's
    /// approved-overtime minutes / 60. numeric(7,2), default 0. The OVERTIME earning line in
    /// <see cref="Details"/> carries the per-multiplier basis.
    /// </summary>
    public decimal OvertimeHours { get; set; }

    /// <summary>
    /// Overtime EARNING amount on this slip (US-PAY-010 AC-2/FR-4): sum over the attendance multiplier buckets of
    /// <c>(minutes/60) * hourly_rate * multiplier</c>, where hourly_rate = monthly_basic / (working_days *
    /// standard_hours_per_day). numeric(18,2), default 0. Included in <see cref="GrossEarnings"/>.
    /// </summary>
    public decimal OvertimeAmount { get; set; }

    /// <summary>
    /// Leave-encashment days applied within this run (US-PAY-010 AC-3/FR-5), when an encashment earning
    /// adjustment was picked up for the employee in this period. numeric(5,2), default 0.
    /// </summary>
    public decimal LeaveEncashmentDays { get; set; }

    /// <summary>
    /// Leave-encashment EARNING amount applied within this run (US-PAY-010 AC-3/FR-5) = eligible_days *
    /// daily_rate. numeric(18,2), default 0. Included in <see cref="GrossEarnings"/>.
    /// </summary>
    public decimal LeaveEncashmentAmount { get; set; }
}
