using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Goal-setting service for managers/HR (US-PRF-001). All operations are tenant-scoped via ITenantContext
/// + the EF global query filter (NFR-2). Authorization (BR-4 — direct manager via Performance.SetGoal.Team,
/// or HR via Performance.SetGoal.All) and the goal-setting-window gate (BR-1/AC-5) are enforced here, not
/// just at the controller, so the rules hold regardless of entry point.
/// </summary>
public interface IGoalService
{
    /// <summary>
    /// Creates a goal for a direct report (FR-1). Fails if: the caller is not the employee's direct
    /// manager and lacks Performance.SetGoal.All (BR-4 → 403); the cycle's goal-setting window is closed
    /// (BR-1/AC-5 → 409); the employee already has 10 goals (BR-2 → 409); or the resulting weights would
    /// exceed 100% (FR-3/AC-3 → 422). On success sends an in-app notification to the employee (AC-2/FR-7).
    /// </summary>
    Task<Result<GoalDto>> CreateAsync(GoalInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing goal (FR-1). Same authz + window gate as create. Optimistic-concurrency aware
    /// (NFR-4): a concurrent write raises a 409. Notifies the employee of the modification (AC-2/FR-7).
    /// </summary>
    Task<Result<GoalDto>> UpdateAsync(Guid goalId, GoalInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a goal (FR-1). Same authz + window gate as create. Enforces the minimum-1 rule lazily
    /// (deletion is allowed; the 100%-sum / 1-10 count rules are validated when goals are submitted/saved).
    /// </summary>
    Task<Result> DeleteAsync(Guid goalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all goals for an employee within a cycle plus the rolled-up weight total (AC-2/AC-3).
    /// Caller must be the direct manager (SetGoal.Team / View.Team) or hold SetGoal.All / View.All.
    /// </summary>
    Task<Result<EmployeeGoalsDto>> GetEmployeeGoalsAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the team goals dashboard for the calling manager + cycle: each direct report with their
    /// goal count, total weight, and aggregate status (AC-4).
    /// </summary>
    Task<Result<TeamGoalsDashboardDto>> GetTeamDashboardAsync(
        Guid cycleId, CancellationToken cancellationToken = default);
}
