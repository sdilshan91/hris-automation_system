using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Inputs for an HR-triggered leave-encashment (US-PAY-010 AC-3/FR-5). The encashment amount =
/// <c>EligibleDays * daily_rate</c> where <c>daily_rate = monthly_basic / working_days</c>; it is added as an
/// EARNING adjustment (a Bonus) to the next payroll run for the period via the US-PAY-007 adjustment mechanism.
/// </summary>
/// <param name="EmployeeId">The employee whose leave is being encashed.</param>
/// <param name="LeaveTypeId">
/// The leave type being encashed. BR-6: must have encashment enabled. When supplied, the service validates the
/// type's <c>Encashable</c> flag and caps <see cref="EligibleDays"/> at the type's MaxEncashDays. Null skips the
/// type-config validation (HR overrides with a manual day count — see the documented BR-6 gap note).
/// </param>
/// <param name="EligibleDays">Number of unused days eligible for encashment (BR-6: balance over carry-forward).</param>
/// <param name="PayMonth">Target pay-period month (1-12). The adjustment is created for the next available run.</param>
/// <param name="PayYear">Target pay-period year.</param>
/// <param name="IsTaxable">Whether the encashment is taxable (default true — encashment is usually taxable income).</param>
public sealed record LeaveEncashmentInput(
    Guid EmployeeId,
    Guid? LeaveTypeId,
    decimal EligibleDays,
    int PayMonth,
    int PayYear,
    bool IsTaxable);

/// <summary>
/// HR-triggered leave encashment (US-PAY-010 AC-3/FR-5). Computes the encashment amount from the employee's
/// current monthly BASIC and the period working-days, then creates an EARNING adjustment (Bonus) for the next
/// payroll run by reusing <see cref="IPayrollAdjustmentService"/> (US-PAY-007). Tenant-scoped via ITenantContext +
/// the EF global query filter (AC-5/FR-8).
/// </summary>
public interface ILeaveEncashmentService
{
    /// <summary>
    /// AC-3/FR-5: processes a leave encashment. Validates the employee + active salary (BR-1) and, when a leave
    /// type is supplied, its encashment eligibility (BR-6). Returns the created adjustment + the computed amount.
    /// </summary>
    Task<Result<LeaveEncashmentResultDto>> ProcessAsync(LeaveEncashmentInput input, CancellationToken cancellationToken = default);
}
