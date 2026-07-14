using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A computed Full-and-Final (F&amp;F) settlement for a departing employee (ISSUE-294 Phase 1). A distinct
/// one-time financial event — NOT a <see cref="PayrollSlip"/> (which requires a PayrollRunId + one-slip-per-run
/// guards). Created by <c>RealPayrollFnFIntegration.TriggerFinalSettlementAsync</c> when offboarding completes.
///
/// <para>Idempotent on <see cref="OffboardingInstanceId"/> (a UNIQUE index): a retried offboarding-complete
/// returns the existing settlement rather than double-computing.</para>
///
/// <para>Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + the dormant Postgres
/// RLS <c>tenant_isolation</c> policy shipped in its migration.</para>
/// </summary>
public sealed class FinalSettlement : BaseEntity, IAuditExempt
{
    /// <summary>The departing employee.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>The offboarding instance that triggered this settlement — UNIQUE (idempotency key).</summary>
    public Guid OffboardingInstanceId { get; set; }

    /// <summary>The authoritative separation date (from offboarding). Drives pro-ration + the policy/period lookup.</summary>
    public DateOnly LastWorkingDay { get; set; }

    /// <summary>The employee's resolved tax country (Location → tenant default → sole-rule fallback). Null = unresolved.</summary>
    public string? CountryCode { get; set; }

    /// <summary>The fiscal year whose statutory rules were resolved (empty when statutory was skipped / none configured).</summary>
    public string FiscalYear { get; set; } = string.Empty;

    /// <summary>Pro-rated final-month gross (earnings + reimbursements) included in the settlement.</summary>
    public decimal ProRatedGross { get; set; }

    /// <summary>Total employee-side statutory deductions applied to the settlement.</summary>
    public decimal StatutoryTotal { get; set; }

    /// <summary>Total forfeitable-leave encashment included in the settlement.</summary>
    public decimal LeaveEncashmentTotal { get; set; }

    /// <summary>Net payable = ProRatedGross + LeaveEncashmentTotal − StatutoryTotal, floored at 0 (never negative).</summary>
    public decimal NetPayable { get; set; }

    /// <summary>The <see cref="TenantFnFPolicy.EffectiveFrom"/> of the policy version that governed this settlement.</summary>
    public DateOnly PolicyEffectiveFrom { get; set; }

    /// <summary>
    /// Snapshot of the governing policy's <see cref="TenantFnFPolicy.FinalPeriodOwnedBySettlement"/> at
    /// computation time. Read by the payroll-run double-pay guard so it never re-resolves policy.
    /// </summary>
    public bool FinalPeriodOwnedBySettlement { get; set; }

    /// <summary>
    /// True when statutory was requested but SKIPPED because the tax country could not be resolved (money-critical
    /// fail-closed — never mis-tax). <see cref="Notes"/> carries the reason.
    /// </summary>
    public bool StatutorySkipped { get; set; }

    /// <summary>Free-form flag/reason (e.g. why statutory was skipped). Null when nothing to flag.</summary>
    public string? Notes { get; set; }

    /// <summary>When the settlement was computed (UTC).</summary>
    public DateTime ComputedAtUtc { get; set; }

    /// <summary>Settlement lifecycle status (Phase 1: always Computed).</summary>
    public FinalSettlementStatus Status { get; set; } = FinalSettlementStatus.Computed;

    // ── Navigation ─────────────────────────────────────────────────
    public List<FinalSettlementLine> Lines { get; set; } = new();
}

/// <summary>One labelled line on a <see cref="FinalSettlement"/> (ISSUE-294 Phase 1).</summary>
public sealed class FinalSettlementLine : BaseEntity, IAuditExempt
{
    /// <summary>FK to the owning settlement.</summary>
    public Guid FinalSettlementId { get; set; }

    /// <summary>Display label (e.g. "Basic Salary", "PAYE Income Tax", "Annual Leave encashment").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Line amount (always positive; <see cref="Type"/> determines whether it adds or subtracts).</summary>
    public decimal Amount { get; set; }

    /// <summary>Line classification — Earning/Encashment add to the net; Statutory subtracts.</summary>
    public FinalSettlementLineType Type { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public FinalSettlement? Settlement { get; set; }
}
