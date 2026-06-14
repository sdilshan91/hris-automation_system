namespace HRM.Application.Features.Holidays.DTOs;

/// <summary>
/// DTO for a holiday (US-LV-007).
/// </summary>
public sealed record HolidayDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public string Type { get; init; } = "Public";
    public Guid? LocationId { get; init; }
    public string? LocationName { get; init; }
    public string? Description { get; init; }
    public bool IsRecurring { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Request body for creating a holiday (US-LV-007 AC-1).
/// </summary>
public sealed record CreateHolidayRequest
{
    public string Name { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public string Type { get; init; } = "Public";
    public Guid? LocationId { get; init; }
    public string? Description { get; init; }
    public bool IsRecurring { get; init; }
}

/// <summary>
/// Request body for updating a holiday (US-LV-007 FR-1).
/// </summary>
public sealed record UpdateHolidayRequest
{
    public string Name { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public string Type { get; init; } = "Public";
    public Guid? LocationId { get; init; }
    public string? Description { get; init; }
    public bool IsRecurring { get; init; }
}

/// <summary>
/// Per-row error from a CSV holiday import (US-LV-007 FR-4, AC-3).
/// </summary>
public sealed record HolidayImportRowError
{
    public int RowNumber { get; init; }
    public string Field { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Result summary of a CSV holiday import (US-LV-007 FR-4, AC-3).
/// </summary>
public sealed record HolidayImportResult
{
    public int Total { get; init; }
    public int Created { get; init; }
    public int Failed { get; init; }
    public IReadOnlyList<HolidayImportRowError> Errors { get; init; } = [];
}
