using HRM.Application.Common.Models;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// One Pending payroll adjustment resolved for inclusion in a payroll run (US-PAY-007 FR-3). Produced by
/// <see cref="IPayrollAdjustmentResolver"/> for the US-PAY-003 run engine so the processor can add it as a
/// payslip line item. All amounts are per-period (monthly) and always positive; <see cref="AdjustmentType"/>
/// decides whether the line is an earning (Bonus/Reimbursement), a deduction, or an arrears/correction line.
/// </summary>
public sealed record ResolvedAdjustment(
    Guid AdjustmentId,
    AdjustmentType AdjustmentType,
    decimal Amount,
    string Description,
    bool IsTaxable,
    Guid? ReferencePayrollSlipId);

/// <summary>
/// The set of Pending adjustments resolved for one employee in one payroll period (US-PAY-007 FR-3).
/// </summary>
public sealed record EmployeeAdjustments(IReadOnlyList<ResolvedAdjustment> Adjustments)
{
    public static readonly EmployeeAdjustments Empty = new([]);
    public bool IsEmpty => Adjustments.Count == 0;
}

/// <summary>
/// Resolves the Pending payroll adjustments in effect for a payroll period (US-PAY-007 FR-3) and, after a run
/// persists slips, marks the included adjustments Applied (FR-4). Wired ADDITIVELY into the US-PAY-003
/// <c>PayrollRunProcessor</c> exactly like <see cref="IStatutoryDeductionResolver"/> was: when NO adjustments
/// exist for an employee+period the resolver returns <see cref="EmployeeAdjustments.Empty"/> and the processor
/// behaves exactly as before, so every existing US-PAY-003/006 test stays green. All reads/writes are
/// tenant-scoped via the EF global query filter (AC-5).
/// </summary>
public interface IPayrollAdjustmentResolver
{
    /// <summary>
    /// FR-3: loads ALL Pending adjustments for the (period) once and groups them by employee, so the processor
    /// can look each employee up without an N+1. Returns an empty map when no adjustments exist for the period.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, EmployeeAdjustments>> ResolveForPeriodAsync(
        int payYear, int payMonth, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-4: marks the given adjustment ids <see cref="AdjustmentStatus.Applied"/> with
    /// <paramref name="payrollRunId"/>, preventing double application on a re-run. Called by the processor at
    /// slip-persist time (the run's compute is complete and slips are saved). Idempotent: already-Applied rows
    /// are left untouched. Does NOT call SaveChanges — the caller batches it into its own persist.
    /// </summary>
    Task MarkAppliedAsync(
        IReadOnlyCollection<Guid> adjustmentIds, Guid payrollRunId, CancellationToken cancellationToken = default);
}
