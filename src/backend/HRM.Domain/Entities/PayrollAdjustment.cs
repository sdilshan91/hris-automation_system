using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// An ad-hoc payroll adjustment for one employee in one pay period (US-PAY-007 FR-1) — a bonus, one-time
/// deduction, reimbursement, or correction/arrears. The payroll-run engine (US-PAY-003) picks up all
/// <see cref="AdjustmentStatus.Pending"/> adjustments for the run's period and adds them as payslip line
/// items (FR-3), then marks them <see cref="AdjustmentStatus.Applied"/> with
/// <see cref="AppliedInPayrollRunId"/> when the run persists slips (FR-4), preventing double application.
///
/// <para>Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + the
/// <c>TenantInterceptor</c> (Postgres RLS is the deferred US-PLT-002). Maps to the "payroll_adjustment"
/// table.</para>
/// </summary>
public sealed class PayrollAdjustment : BaseEntity
{
    /// <summary>Employee this adjustment applies to (FR-1, required). Must have an active salary assignment (BR-1).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Adjustment kind (FR-1, required). Drives the payslip line type (earning vs deduction vs arrears).</summary>
    public AdjustmentType AdjustmentType { get; set; }

    /// <summary>Flat money amount (FR-1, required). Always positive; the type decides whether it adds or subtracts.
    /// numeric(18,2).</summary>
    public decimal Amount { get; set; }

    /// <summary>Reason / description (FR-1, required).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Pay-period month this adjustment targets, 1-12 (FR-1, required).</summary>
    public int ApplicablePayMonth { get; set; }

    /// <summary>Pay-period year this adjustment targets (FR-1, required).</summary>
    public int ApplicablePayYear { get; set; }

    /// <summary>Whether a bonus/reimbursement is taxable (BR-2/BR-4, default false).</summary>
    public bool IsTaxable { get; set; }

    /// <summary>Whether this is a recurring adjustment that auto-creates future pendings (FR-5/BR-6, default false).</summary>
    public bool IsRecurring { get; set; }

    /// <summary>Last month (1-12) of the recurrence window, inclusive (FR-5); null when not recurring.</summary>
    public int? RecurrenceEndMonth { get; set; }

    /// <summary>Last year of the recurrence window, inclusive (FR-5); null when not recurring.</summary>
    public int? RecurrenceEndYear { get; set; }

    /// <summary>Current lifecycle status (FR-4/FR-6). Stored as varchar(20).</summary>
    public AdjustmentStatus Status { get; set; } = AdjustmentStatus.Pending;

    /// <summary>The payroll run that applied this adjustment (FR-4); null while Pending. Prevents double application.</summary>
    public Guid? AppliedInPayrollRunId { get; set; }

    /// <summary>For a <see cref="AdjustmentType.Correction"/>, the original payslip being corrected (BR-5/FR-7); null otherwise.</summary>
    public Guid? ReferencePayrollSlipId { get; set; }

    /// <summary>Path to the optional supporting document in tenant blob storage (AC-3/NFR-5); null when none.</summary>
    public string? SupportingDocumentPath { get; set; }

    /// <summary>
    /// Links a recurring occurrence back to the originating (first) adjustment so HR can cancel the whole
    /// series (BR-6). Null on the originating row and on non-recurring adjustments.
    /// </summary>
    public Guid? RecurringSeriesId { get; set; }
}
