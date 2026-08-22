using HRM.Domain.Enums;

namespace HRM.Application.Features.Onboarding.DTOs;

/// <summary>US-ONB-005 AC-3 dashboard: a single offboarding task / clearance item.</summary>
public sealed record OffboardingTaskDto
{
    public Guid Id { get; init; }
    public ClearanceCategory ClearanceCategory { get; init; }
    public string ClearanceCategoryName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public OnboardingResponsibleRole ResponsibleRole { get; init; }
    public string ResponsibleRoleName { get; init; } = string.Empty;
    public Guid? ResponsibleUserId { get; init; }
    public DateOnly DueDate { get; init; }
    public OnboardingTaskStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public bool IsMandatory { get; init; }
    public int SortOrder { get; init; }

    /// <summary>The clearance decision: "approved", "pending_issues", or null when not yet decided (AC-3).</summary>
    public string? ClearanceStatus { get; init; }
    public string? Remarks { get; init; }
    public Guid? LinkedAssetId { get; init; }
    public DateTime? CompletedAt { get; init; }
}

/// <summary>
/// US-ONB-005 AC-3/FR-4 dashboard: per-department clearance status (the traffic-light summary). One row per
/// <see cref="ClearanceCategory"/> present on the instance.
/// </summary>
public sealed record DepartmentClearanceDto
{
    public ClearanceCategory ClearanceCategory { get; init; }
    public string ClearanceCategoryName { get; init; } = string.Empty;

    /// <summary>"cleared" (green), "issues" (yellow/red), or "pending" (no decision yet) — AC-3/FR-4.</summary>
    public string Status { get; init; } = string.Empty;

    public int TotalTasks { get; init; }
    public int ApprovedTasks { get; init; }
    public int PendingIssueTasks { get; init; }
    public int UndecidedTasks { get; init; }
    public List<OffboardingTaskDto> Tasks { get; init; } = [];
}

/// <summary>US-ONB-005 AC-3/FR-4: the overall clearance summary across all departments.</summary>
public sealed record ClearanceSummaryDto
{
    /// <summary>True only when every department has approved all its mandatory clearances (AC-3 "fully cleared").</summary>
    public bool FullyCleared { get; init; }
    public int TotalDepartments { get; init; }
    public int ClearedDepartments { get; init; }
    public int PendingDepartments { get; init; }
}

/// <summary>
/// US-ONB-005 AC-3 / FR-4: the full offboarding instance with tasks grouped by clearance category, the
/// per-department status, the overall clearance summary, and progress. Returned by initiate + get.
/// </summary>
public sealed record OffboardingInstanceDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public Guid? TemplateId { get; init; }
    public string TemplateName { get; init; } = string.Empty;
    public DateOnly LastWorkingDay { get; init; }
    public OffboardingReason Reason { get; init; }
    public string ReasonName { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public OffboardingStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public Guid? InitiatedByUserId { get; init; }
    public DateTime? CompletedAt { get; init; }

    /// <summary>Overall task progress (completed / total), 0-100 (FR-4).</summary>
    public int ProgressPercent { get; init; }
    public int TotalTasks { get; init; }
    public int CompletedTasks { get; init; }

    public ClearanceSummaryDto ClearanceSummary { get; init; } = new();

    /// <summary>Tasks grouped by clearance department, with per-department status (AC-3 dashboard data).</summary>
    public List<DepartmentClearanceDto> Departments { get; init; } = [];

    /// <summary>
    /// AC-5 / BR-2: every mandatory item that currently blocks completion, in display order. Empty means
    /// nothing blocks.
    ///
    /// <para>
    /// Projected from <c>OffboardingCompletionGate</c> — the same code the completion endpoint enforces with
    /// — so the dashboard can disable the Complete button and say exactly why <i>without re-deriving the
    /// rule</i>. It used to re-derive it, and the copy was wrong: it compared a task's clearance against
    /// "cleared", a department-level token no task ever carries, so the button never enabled.
    /// </para>
    /// </summary>
    public IReadOnlyList<PendingMandatoryItemDto> PendingMandatoryItems { get; init; } = [];

    /// <summary>
    /// AC-5: whether completion would succeed right now — no blocking mandatory items, and not already
    /// completed (BR-6 makes completion irreversible).
    /// </summary>
    public bool CanComplete { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>US-ONB-005 AC-5: a single pending mandatory item that blocks completion.</summary>
public sealed record PendingMandatoryItemDto
{
    public Guid TaskId { get; init; }
    public string Title { get; init; } = string.Empty;
    public ClearanceCategory ClearanceCategory { get; init; }
    public string ClearanceCategoryName { get; init; } = string.Empty;

    /// <summary>Why it blocks: "not_completed" or "clearance_not_approved".</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// US-ONB-005 AC-4/AC-5: the result of a completion attempt. On success <see cref="Completed"/> is true and
/// <see cref="Instance"/> is the completed offboarding. When blocked (AC-5) the controller returns 409 with
/// <see cref="PendingItems"/> populated and <see cref="Completed"/> false.
/// </summary>
public sealed record CompleteOffboardingResultDto
{
    public bool Completed { get; init; }
    public OffboardingInstanceDto? Instance { get; init; }
    public IReadOnlyList<PendingMandatoryItemDto> PendingItems { get; init; } = [];

    /// <summary>FR-6: the F&amp;F settlement reference returned by the Payroll integration seam (success only).</summary>
    public Guid? FinalSettlementRef { get; init; }
}
