using HRM.Domain.Enums;

namespace HRM.Application.Features.Performance.DTOs;

/// <summary>A single goal as returned to the manager/employee (US-PRF-001).</summary>
public sealed record GoalDto
{
    public Guid Id { get; init; }
    public Guid CycleId { get; init; }
    public Guid EmployeeId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public GoalCategory Category { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public int Weight { get; init; }
    public string TargetValue { get; init; } = string.Empty;
    public string MeasurementUnit { get; init; } = string.Empty;
    public DateOnly DueDate { get; init; }
    public Guid? ParentGoalId { get; init; }
    public GoalStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>
    /// Optimistic concurrency token (NFR-4, BUG-057). Maps to the goal row's PostgreSQL xmin. Clients
    /// must echo this back on update; a stale value yields 409 Conflict. Mirrors
    /// <c>EmployeeProfileDto.RowVersion</c>.
    /// </summary>
    public uint RowVersion { get; init; }
}

/// <summary>
/// The set of goals for one employee + cycle plus the rolled-up weight total (US-PRF-001 AC-2/AC-3).
/// </summary>
public sealed record EmployeeGoalsDto
{
    public Guid EmployeeId { get; init; }
    public Guid CycleId { get; init; }
    public int TotalWeight { get; init; }
    public bool IsGoalSettingOpen { get; init; }
    public IReadOnlyList<GoalDto> Goals { get; init; } = [];
}

/// <summary>One team member's goal-setting status on the team dashboard (US-PRF-001 AC-4).</summary>
public sealed record TeamGoalStatusDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;
    public int GoalCount { get; init; }
    public int TotalWeight { get; init; }

    /// <summary>
    /// Aggregate goal-setting status for the member: "NotStarted" (no goals), "Draft", "Submitted",
    /// or "Acknowledged" (AC-4). Derived from the member's goals for the cycle.
    /// </summary>
    public string Status { get; init; } = "NotStarted";
}

/// <summary>The team goals dashboard for a manager + cycle (US-PRF-001 AC-4).</summary>
public sealed record TeamGoalsDashboardDto
{
    public Guid CycleId { get; init; }
    public IReadOnlyList<TeamGoalStatusDto> Members { get; init; } = [];
}

/// <summary>API request body for creating a goal (US-PRF-001 FR-2).</summary>
public sealed record CreateGoalRequest(
    Guid CycleId,
    Guid EmployeeId,
    string Title,
    string? Description,
    GoalCategory Category,
    int Weight,
    string TargetValue,
    string MeasurementUnit,
    DateOnly DueDate,
    Guid? ParentGoalId);

/// <summary>API request body for updating a goal (US-PRF-001 FR-1).</summary>
public sealed record UpdateGoalRequest(
    string Title,
    string? Description,
    GoalCategory Category,
    int Weight,
    string TargetValue,
    string MeasurementUnit,
    DateOnly DueDate,
    Guid? ParentGoalId,
    // BUG-057: optimistic concurrency token (the xmin the client read). Echoed back to guard the UPDATE.
    uint RowVersion = 0);

/// <summary>
/// One goal in a bulk full-replace save (US-PRF-001, BUG-243). <see cref="Id"/> present → update the
/// matching goal; absent (or unknown) → create a new Draft goal. Existing goals for the (employee, cycle)
/// not present in the incoming set are soft-deleted.
/// </summary>
public sealed record SaveGoalItem(
    Guid? Id,
    string Title,
    string? Description,
    GoalCategory Category,
    int Weight,
    string TargetValue,
    string MeasurementUnit,
    DateOnly DueDate,
    Guid? ParentGoalId);

/// <summary>
/// API request body for the bulk full-replace SaveGoals endpoint (US-PRF-001, BUG-243). The incoming
/// <see cref="Goals"/> list is the complete desired set for the (employee, cycle) — items are created/updated,
/// and any existing goal absent from the list is soft-deleted, in a single atomic SaveChanges.
/// </summary>
public sealed record SaveGoalsRequest(List<SaveGoalItem> Goals);

/// <summary>
/// API request body for the finalize/lock endpoint (BUG-056). Locks the goal set for (EmployeeId, CycleId)
/// once its weights sum to exactly 100%.
/// </summary>
public sealed record FinalizeGoalsRequest(Guid EmployeeId, Guid CycleId);

/// <summary>
/// API request body for the re-open/unlock endpoint (DF-46, BUG-056 follow-up). Re-opens the finalized
/// goal set for (EmployeeId, CycleId), transitioning its goals back to Acknowledged. <see cref="Reason"/>
/// is mandatory and recorded in the audit trail.
/// </summary>
public sealed record ReopenGoalsRequest(Guid EmployeeId, Guid CycleId, string Reason);

/// <summary>Service-layer input for create/update of a single goal (decouples handler records from the service).</summary>
public sealed record GoalInput(
    Guid CycleId,
    Guid EmployeeId,
    string Title,
    string? Description,
    GoalCategory Category,
    int Weight,
    string TargetValue,
    string MeasurementUnit,
    DateOnly DueDate,
    Guid? ParentGoalId,
    // BUG-057: expected concurrency token for update (ignored on create). 0 = not supplied.
    uint RowVersion = 0);
