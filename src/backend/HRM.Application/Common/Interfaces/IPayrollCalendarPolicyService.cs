using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Read/write access to the per-tenant, effective-dated payroll calendar policy (US-ATT-011 AC-4 / CAL-5).
/// The policy governs whether public holidays are excluded from the payroll working-days count; a new
/// configuration creates a NEW effective-dated version (never mutates history), mirroring the F&amp;F policy.
/// All reads/writes are tenant-scoped via the EF global query filter.
/// </summary>
public interface IPayrollCalendarPolicyService
{
    /// <summary>Creates a new effective-dated policy version for the tenant (replaces any version on the same date).</summary>
    Task<Result<PayrollCalendarPolicyDto>> CreateAsync(CreatePayrollCalendarPolicyInput input, CancellationToken cancellationToken = default);

    /// <summary>Lists all of the tenant's policy versions, newest effective-from first.</summary>
    Task<Result<IReadOnlyList<PayrollCalendarPolicyDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the policy version in effect on <paramref name="asOf"/> (latest EffectiveFrom ≤ asOf). When none
    /// is configured, returns the code-default (holidays NOT excluded) so the effective behaviour is visible.
    /// </summary>
    Task<Result<PayrollCalendarPolicyDto>> GetEffectiveAsync(DateOnly asOf, CancellationToken cancellationToken = default);
}

/// <summary>Input for creating a new effective-dated payroll calendar policy version (US-ATT-011 AC-4 / CAL-5).</summary>
public sealed record CreatePayrollCalendarPolicyInput(
    DateOnly EffectiveFrom,
    bool ExcludeHolidaysFromWorkingDays,
    bool IsActive);
