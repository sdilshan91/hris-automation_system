namespace HRM.Domain.Entities;

/// <summary>
/// A single resolved salary-component value for an employee under an assigned salary structure
/// (US-PAY-002 FR-1/FR-2). One row per component in the assigned structure, holding the annual + derived
/// monthly amount calculated from the employee's CTC and the structure's component rules. Tenant-scoped
/// via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>. Maps to
/// the "employee_salary_component" table.
///
/// <para>
/// Validity period: <see cref="EffectiveFrom"/> / <see cref="EffectiveTo"/> (null = current). A new
/// current-or-past assignment supersedes the prior one by stamping the prior rows' <see cref="EffectiveTo"/>
/// (BR-1); a future-dated assignment leaves the current rows untouched until its date arrives (BR-2).
/// </para>
/// </summary>
public sealed class EmployeeSalaryComponent : BaseEntity, IAuditExempt
{
    /// <summary>FK to the employee this compensation row belongs to (FR-1, required).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>FK to the assigned salary structure (FR-1, required).</summary>
    public Guid SalaryStructureId { get; set; }

    /// <summary>FK to the salary component this row resolves (FR-1, required).</summary>
    public Guid SalaryComponentId { get; set; }

    /// <summary>Resolved annual amount for this component (FR-2). numeric(18,2) (§7).</summary>
    public decimal AnnualAmount { get; set; }

    /// <summary>Derived monthly amount = <see cref="AnnualAmount"/> / 12 (§7, computed at assignment). numeric(18,2).</summary>
    public decimal MonthlyAmount { get; set; }

    /// <summary>True when the HR officer overrode the calculated value for this component (AC-3, default false).</summary>
    public bool IsOverride { get; set; }

    /// <summary>Date from which this resolved value is effective (FR-1, required).</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Date the value stops being effective; null = currently active (BR-1/BR-2).</summary>
    public DateOnly? EffectiveTo { get; set; }
}
