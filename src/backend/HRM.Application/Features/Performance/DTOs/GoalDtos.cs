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
    Guid? ParentGoalId);

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
    Guid? ParentGoalId);
