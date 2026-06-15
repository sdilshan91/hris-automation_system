namespace HRM.Domain.Entities;

/// <summary>
/// One component line on an employee's payslip (US-PAY-003 FR-5f). Denormalizes the component name + type
/// at compute time so the historical payslip is stable even if the component is later renamed/deleted.
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>.
/// Maps to the "payroll_slip_detail" table.
/// </summary>
public sealed class PayrollSlipDetail : BaseEntity
{
    /// <summary>FK to the owning payslip (FR-5, required).</summary>
    public Guid PayrollSlipId { get; set; }

    /// <summary>FK to the salary component this line resolves (FR-5, required).</summary>
    public Guid SalaryComponentId { get; set; }

    /// <summary>Component display name, denormalized for history (FR-5, required, max 100 chars).</summary>
    public string ComponentName { get; set; } = string.Empty;

    /// <summary>Component classification (Earning/Deduction/Statutory/Reimbursement), denormalized (FR-5, required, max 20 chars).</summary>
    public string ComponentType { get; set; } = string.Empty;

    /// <summary>Resolved amount for this component on this slip (FR-5). numeric(18,2), required.</summary>
    public decimal Amount { get; set; }

    /// <summary>Human-readable derivation note, e.g. "40% of 50000" or "LOP 3.00/22.00 days" (FR-5, nullable).</summary>
    public string? CalculationBasis { get; set; }
}
