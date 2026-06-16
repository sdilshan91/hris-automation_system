using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Goal-tracking / progress-update service (US-PRF-009). Drives the workflow: an employee lists their active goals
/// with current progress (AC-1) and posts append-only progress updates during the active cycle window (AC-2/FR-1/
/// FR-2/BR-1); the goal's progress timeline is read back chronologically (AC-3/FR-3); a manager sees a weighted
/// team goal-completion summary scoped to their direct reports (AC-4/FR-4) and can comment on updates (FR-8).
///
/// IMMUTABILITY (NFR-3): there is NO update or delete path for a posted progress update — the ordered set of rows
/// IS the history. BR-2 auto-sets Completed at 100% (overridable); BR-3 (Blocked) notifies the manager + HR; the
/// manager is notified on every posted update (FR-5 — via the log-only seam; real-time/SignalR is a deferred hook,
/// US-NTF-001). Visibility (BR-5): updates are readable by the employee, their manager and HR only (not peers).
/// Every read/write is tenant-scoped via the EF global query filter (NFR-2).
/// </summary>
public interface IGoalProgressService
{
    /// <summary>
    /// Lists the calling employee's active goals for the current cycle with each goal's CURRENT progress derived
    /// from its latest update (AC-1). The caller is resolved from the session; tenant-scoped (NFR-2).
    /// </summary>
    Task<Result<IReadOnlyList<MyGoalProgressDto>>> GetMyGoalsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts an APPEND-ONLY progress update against one of the caller's goals (AC-2/FR-1/FR-2). Allowed only during
    /// the active cycle window (BR-1); BR-2 auto-sets Completed at 100% unless the caller chose a different status;
    /// notifies the manager (FR-5) and, for Blocked, the manager + HR (BR-3). Returns the goal's updated timeline.
    /// </summary>
    Task<Result<GoalTimelineDto>> AddProgressUpdateAsync(AddGoalProgressInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one goal's chronological progress timeline + comment thread (AC-3/FR-3/FR-8). VISIBILITY-restricted
    /// (BR-5): only the goal's employee, their manager or HR — anyone else gets 403.
    /// </summary>
    Task<Result<GoalTimelineDto>> GetGoalTimelineAsync(Guid goalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the manager's team goal-progress summary (AC-4): one row per in-scope employee with the weighted
    /// overall completion % (FR-4), at-risk + needs-attention counts and the last-update date. Scope is the
    /// caller's direct reports (<c>Performance.Review.Team</c>) or all in-tenant employees (<c>Review.All</c>).
    /// </summary>
    Task<Result<IReadOnlyList<TeamGoalProgressRowDto>>> GetTeamSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one of a manager's in-scope reports' goals with current progress (AC-4 drill-down). Scope-restricted
    /// to a direct report (Review.Team) or any in-tenant employee (Review.All); else 403.
    /// </summary>
    Task<Result<IReadOnlyList<MyGoalProgressDto>>> GetEmployeeGoalsAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a manager/HR comment to a goal's thread (FR-8), optionally anchored to a specific update. The author
    /// must be the goal owner's manager or HR (BR-5). Returns the goal's updated timeline.
    /// </summary>
    Task<Result<GoalTimelineDto>> AddCommentAsync(AddGoalCommentInput input, CancellationToken cancellationToken = default);
}
