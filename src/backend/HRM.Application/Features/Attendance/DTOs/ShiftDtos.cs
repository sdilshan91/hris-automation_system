namespace HRM.Application.Features.Attendance.DTOs;

/// <summary>
/// A single rotation step in the API contract (US-ATT-005 FR-7). <c>order</c> is 0-based;
/// <c>shiftId</c> references a concrete (SINGLE) shift; <c>durationDays</c> is how many days it covers.
/// </summary>
public sealed record RotationStepDto
{
    public int Order { get; init; }
    public Guid ShiftId { get; init; }
    public int DurationDays { get; init; }
}

/// <summary>
/// Rotation pattern for a ROTATING shift (US-ATT-005 FR-7). <c>referenceStartDate</c> is the anchor
/// the cycle is measured from; the applicable step for a date is computed by day-index modulo
/// <c>cycleLengthDays</c>.
/// </summary>
public sealed record RotationDto
{
    public int CycleLengthDays { get; init; }
    public DateOnly ReferenceStartDate { get; init; }
    public IReadOnlyList<RotationStepDto> Steps { get; init; } = Array.Empty<RotationStepDto>();
}

/// <summary>
/// Shift response DTO (US-ATT-005). Matches the pinned API contract verbatim. Times are "HH:mm"
/// strings (null for FLEXIBLE); workingDays is 1=Mon..7=Sun.
/// </summary>
public record ShiftDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? StartTime { get; init; }
    public string? EndTime { get; init; }
    public int BreakDurationMinutes { get; init; }
    public int GracePeriodMinutes { get; init; }
    public decimal? MinimumHours { get; init; }
    public IReadOnlyList<int> WorkingDays { get; init; } = Array.Empty<int>();
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public int AssignedEmployeeCount { get; init; }
    public RotationDto? Rotation { get; init; }
}

/// <summary>
/// Resolved-shift response (US-ATT-005 FR-5/FR-7). Extends <see cref="ShiftDto"/> with the assignment
/// window and the date it was resolved for.
/// </summary>
public sealed record ResolvedShiftDto : ShiftDto
{
    /// <summary>Effective-from of the assignment that produced this resolution; null when falling back to the tenant default.</summary>
    public DateOnly? EffectiveFrom { get; init; }

    /// <summary>Effective-to of the assignment (null = open); null when falling back to the tenant default.</summary>
    public DateOnly? EffectiveTo { get; init; }

    /// <summary>The date the resolution was computed for.</summary>
    public DateOnly ResolvedForDate { get; init; }
}

/// <summary>
/// Result of a bulk assign operation (US-ATT-005 AC-2/AC-3/FR-3).
/// </summary>
public sealed record AssignmentResultDto
{
    public int AssignedCount { get; init; }
    public IReadOnlyList<Guid> EmployeeShiftIds { get; init; } = Array.Empty<Guid>();
}

// ── Request bodies ────────────────────────────────────────────────────

/// <summary>
/// Request body for creating/updating a shift (US-ATT-005). Times are "HH:mm" (24h) strings,
/// optional for FLEXIBLE. <c>rotation</c> is required for ROTATING only.
/// </summary>
public sealed record ShiftRequest
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? StartTime { get; init; }
    public string? EndTime { get; init; }
    public int BreakDurationMinutes { get; init; }
    public int GracePeriodMinutes { get; init; }
    public decimal? MinimumHours { get; init; }
    public IReadOnlyList<int> WorkingDays { get; init; } = Array.Empty<int>();
    public RotationRequest? Rotation { get; init; }
}

/// <summary>Rotation pattern in a create/update request (US-ATT-005 FR-7).</summary>
public sealed record RotationRequest
{
    public int CycleLengthDays { get; init; }
    public DateOnly ReferenceStartDate { get; init; }
    public IReadOnlyList<RotationStepRequest> Steps { get; init; } = Array.Empty<RotationStepRequest>();
}

/// <summary>One rotation step in a create/update request (US-ATT-005 FR-7).</summary>
public sealed record RotationStepRequest
{
    public int Order { get; init; }
    public Guid ShiftId { get; init; }
    public int DurationDays { get; init; }
}

/// <summary>
/// Request body for bulk shift assignment (US-ATT-005 AC-2/AC-3/FR-3).
/// </summary>
public sealed record AssignShiftRequest
{
    public IReadOnlyList<Guid> EmployeeIds { get; init; } = Array.Empty<Guid>();
    public DateOnly EffectiveFrom { get; init; }
}
