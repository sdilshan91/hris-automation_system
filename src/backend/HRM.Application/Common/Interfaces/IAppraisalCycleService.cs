using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Appraisal cycle management for HR (US-PRF-004). All operations are tenant-scoped via ITenantContext + the
/// EF global query filter (NFR-2). Authorization (BR-1 — Performance.SetGoal.All OR Performance.Publish.All)
/// is enforced at the controller; the service enforces the domain rules: phase sequencing/non-overlap/in-range
/// (FR-2/BR-3), participant scoping (FR-3), BR-4 (no two active same-type cycles per employee), the status
/// state machine (FR-7), rating-scale lock on Active (BR-5), cancel-needs-reason (BR-6), and delete-only-if-no
/// -reviews (BR-2). The GoalSetting/SelfAssessment/ManagerReview phases are kept in sync with the legacy cycle
/// window columns so US-PRF-001/002/003 windows are now phase-driven.
/// </summary>
public interface IAppraisalCycleService
{
    /// <summary>Creates a Draft cycle with phases + resolved participants and schedules its Hangfire jobs (AC-1/AC-2).</summary>
    Task<Result<CycleDto>> CreateAsync(CreateCycleInput input, CancellationToken cancellationToken = default);

    /// <summary>Edits a cycle / extends a phase, re-validates dates, reschedules jobs, notifies (AC-5).</summary>
    Task<Result<CycleDto>> UpdateAsync(Guid cycleId, UpdateCycleInput input, CancellationToken cancellationToken = default);

    /// <summary>Clones a cycle's settings/phases under a new name with a shifted window (FR-8).</summary>
    Task<Result<CycleDto>> CloneAsync(CloneCycleInput input, CancellationToken cancellationToken = default);

    /// <summary>Transitions a cycle's status per the FR-7 state machine (Cancel requires a reason, BR-6).</summary>
    Task<Result<CycleDto>> TransitionStatusAsync(
        Guid cycleId, AppraisalCycleStatus targetStatus, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Deletes a Draft cycle — fails if any review activity exists (cancel only, BR-2).</summary>
    Task<Result> DeleteAsync(Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>Returns a single cycle with phases (US-PRF-004 §7).</summary>
    Task<Result<CycleDto>> GetByIdAsync(Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>Lists cycles for the tenant, newest first (optionally filtered by status).</summary>
    Task<Result<IReadOnlyList<CycleSummaryDto>>> ListAsync(
        AppraisalCycleStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the canonical ACTIVE cycle with its current phase + authoritative window flags (AC-3). This is
    /// the endpoint US-PRF-001/002/003 FEs gate their read-only / closed-message rendering off.
    /// </summary>
    Task<Result<ActiveCycleDto>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>The cycle dashboard: timeline + per-phase completion stats + overdue counts (AC-3).</summary>
    Task<Result<CycleDashboardDto>> GetDashboardAsync(Guid cycleId, CancellationToken cancellationToken = default);
}
