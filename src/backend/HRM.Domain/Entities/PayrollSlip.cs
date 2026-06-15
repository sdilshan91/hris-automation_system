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
}
