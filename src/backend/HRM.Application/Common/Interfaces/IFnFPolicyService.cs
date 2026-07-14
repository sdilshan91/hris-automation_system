using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Read/write access to the per-tenant, effective-dated Full-and-Final settlement policy (ISSUE-294 Phase 1).
/// The policy governs which components a final settlement includes; a new configuration creates a NEW
/// effective-dated version (never mutates history), mirroring statutory-rule effective-dating. All reads/writes
/// are tenant-scoped via the EF global query filter.
/// </summary>
public interface IFnFPolicyService
{
    /// <summary>Creates a new effective-dated policy version for the tenant.</summary>
    Task<Result<FnFPolicyDto>> CreateAsync(CreateFnFPolicyInput input, CancellationToken cancellationToken = default);

    /// <summary>Lists all of the tenant's policy versions, newest effective-from first.</summary>
    Task<Result<IReadOnlyList<FnFPolicyDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the policy version in effect on <paramref name="asOf"/> (latest EffectiveFrom ≤ asOf). When none
    /// is configured, returns the safe code-default (all includes on) so the effective behaviour is visible.
    /// </summary>
    Task<Result<FnFPolicyDto>> GetEffectiveAsync(DateOnly asOf, CancellationToken cancellationToken = default);
}

/// <summary>Input for creating a new effective-dated F&amp;F policy version (ISSUE-294 Phase 1).</summary>
public sealed record CreateFnFPolicyInput(
    DateOnly EffectiveFrom,
    bool IncludeProRatedFinalPay,
    bool IncludeStatutory,
    bool IncludeLeaveEncashment,
    bool FinalPeriodOwnedBySettlement,
    bool IsActive);
