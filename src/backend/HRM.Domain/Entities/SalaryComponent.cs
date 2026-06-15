using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A reusable salary component — an earning, deduction, statutory item, or reimbursement (US-PAY-001
/// FR-1). Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter +
/// <c>TenantInterceptor</c> (this codebase enforces tenant isolation with EF query filters; Postgres
/// RLS is the deferred US-PLT-002 — see the payroll module note). Maps to the "salary_component" table.
/// </summary>
public sealed class SalaryComponent : BaseEntity
{
    /// <summary>Display name (FR-1, required, max 100 chars), e.g. "Basic Salary".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Short code, unique per tenant (FR-1/BR-2), e.g. "BASIC". Stored upper-cased.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Component classification (FR-1).</summary>
    public SalaryComponentType Type { get; set; }

    /// <summary>How the component value is derived (FR-1/FR-4).</summary>
    public CalculationMethod CalculationMethod { get; set; }

    /// <summary>
    /// Default fixed amount (when <see cref="CalculationMethod"/> is Fixed) or default percentage
    /// (PercentageOfBasic / PercentageOfGross). Null for Formula. numeric(18,2) (§7/§10).
    /// </summary>
    public decimal? DefaultValue { get; set; }

    /// <summary>Safe arithmetic expression when <see cref="CalculationMethod"/> is Formula (FR-4). Null otherwise.</summary>
    public string? FormulaExpression { get; set; }

    /// <summary>Whether the component is subject to income tax (FR-1, default true).</summary>
    public bool IsTaxable { get; set; } = true;

    /// <summary>Statutory flag (BR-3, default false). Statutory components cannot be removed when compliance is enabled.</summary>
    public bool IsStatutory { get; set; }

    /// <summary>Whether the component is currently active and selectable (FR-1, default true).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Default processing priority for payroll calculation (AC-4, default 0). Lower = earlier.</summary>
    public int ProcessingOrder { get; set; }
}
