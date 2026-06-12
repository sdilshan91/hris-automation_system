namespace HRM.Application.Features.Employees.DTOs;

/// <summary>
/// Request to assign a reporting manager to an employee (US-CHR-011 AC-2).
/// </summary>
public sealed record AssignManagerRequest
{
    /// <summary>
    /// The employee ID to assign as manager. Null to unassign (FR-8).
    /// </summary>
    public Guid? ManagerEmployeeId { get; init; }

    /// <summary>
    /// Optional reason for the change (recorded in employment history).
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Result of a manager assignment operation.
/// </summary>
public sealed record AssignManagerResult
{
    public Guid EmployeeId { get; init; }
    public Guid? PreviousManagerId { get; init; }
    public string? PreviousManagerName { get; init; }
    public Guid? NewManagerId { get; init; }
    public string? NewManagerName { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Request to bulk-assign a manager to multiple employees (US-CHR-011 FR-4, AC-5).
/// </summary>
public sealed record BulkAssignManagerRequest
{
    /// <summary>
    /// Employee IDs to assign the manager to.
    /// </summary>
    public IReadOnlyList<Guid> EmployeeIds { get; init; } = [];

    /// <summary>
    /// The employee ID to assign as manager.
    /// </summary>
    public Guid ManagerEmployeeId { get; init; }

    /// <summary>
    /// Optional reason for the bulk change.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Result of a bulk manager assignment operation (US-CHR-011 AC-5).
/// </summary>
public sealed record BulkAssignManagerResult
{
    public int TotalRequested { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public IReadOnlyList<BulkAssignManagerItemResult> Results { get; init; } = [];
}

/// <summary>
/// Per-employee result within a bulk assignment.
/// </summary>
public sealed record BulkAssignManagerItemResult
{
    public Guid EmployeeId { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? EmployeeName { get; init; }
}

/// <summary>
/// Summary DTO for a direct report (US-CHR-011 FR-5, AC-4).
/// </summary>
public sealed record DirectReportDto
{
    public Guid Id { get; init; }
    public string EmployeeNo { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? JobTitle { get; init; }
    public string? DepartmentName { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
}

/// <summary>
/// Result wrapper for the direct reports query.
/// </summary>
public sealed record DirectReportsResult
{
    public Guid ManagerId { get; init; }
    public string ManagerName { get; init; } = string.Empty;
    public IReadOnlyList<DirectReportDto> DirectReports { get; init; } = [];
}
