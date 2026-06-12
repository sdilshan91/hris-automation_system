using HRM.Domain.Enums;

namespace HRM.Application.Features.Employees.DTOs;

/// <summary>
/// Request body for changing an employee's status (US-CHR-009 FR-3).
/// </summary>
public sealed record ChangeEmployeeStatusRequest
{
    /// <summary>
    /// The new status to transition to.
    /// </summary>
    public EmployeeStatus NewStatus { get; init; }

    /// <summary>
    /// Required reason for the status change.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Required effective date. If in the future, the change is stored
    /// but not applied until the effective date (BR-4).
    /// </summary>
    public DateTime EffectiveDate { get; init; }
}

/// <summary>
/// Response DTO for a status change operation.
/// </summary>
public sealed record ChangeEmployeeStatusResult
{
    public Guid EmployeeId { get; init; }
    public string PreviousStatus { get; init; } = string.Empty;
    public string NewStatus { get; init; } = string.Empty;
    public DateTime EffectiveDate { get; init; }
    public bool IsFutureDated { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// DTO for the valid transitions query.
/// </summary>
public sealed record ValidTransitionsResult
{
    public string CurrentStatus { get; init; } = string.Empty;
    public IReadOnlyList<string> ValidTransitions { get; init; } = [];
}
