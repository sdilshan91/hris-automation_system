namespace HRM.Application.Features.Employees.DTOs;

/// <summary>
/// A single row parsed from the import file (US-CHR-010).
/// </summary>
public sealed record BulkImportRowDto
{
    public int RowNumber { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public string? DateOfJoining { get; init; }
    public string? DepartmentName { get; init; }
    public string? JobTitleName { get; init; }
    public string? EmploymentType { get; init; }
    public string? LocationName { get; init; }
    public string? Status { get; init; }
}

/// <summary>
/// Per-row validation error (US-CHR-010 FR-8).
/// </summary>
public sealed record BulkImportRowError
{
    public int RowNumber { get; init; }
    public string Field { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Summary result of a bulk import operation (US-CHR-010 AC-2/AC-3).
/// </summary>
public sealed record BulkImportResult
{
    /// <summary>Total data rows in the file.</summary>
    public int Total { get; init; }

    /// <summary>Rows successfully imported.</summary>
    public int Success { get; init; }

    /// <summary>Rows that failed validation.</summary>
    public int Failed { get; init; }

    /// <summary>Per-row error details for failed rows.</summary>
    public IReadOnlyList<BulkImportRowError> Errors { get; init; } = [];

    /// <summary>
    /// If non-null, the import was queued as an async background job.
    /// The client should poll for status using this ID.
    /// </summary>
    public Guid? JobId { get; init; }

    /// <summary>
    /// Warning message, e.g. plan limit exceeded (AC-5).
    /// </summary>
    public string? Warning { get; init; }

    /// <summary>
    /// True if the import was processed synchronously and is complete.
    /// </summary>
    public bool IsComplete { get; init; }
}

/// <summary>
/// Pre-validation result when the import would exceed the tenant plan limit (US-CHR-010 AC-5).
/// </summary>
public sealed record BulkImportPlanLimitWarning
{
    public int CurrentCount { get; init; }
    public int MaxAllowed { get; init; }
    public int AvailableSlots { get; init; }
    public int RequestedCount { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Status of an async bulk import job (US-CHR-010 AC-4).
/// </summary>
public sealed record BulkImportJobStatus
{
    public Guid JobId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public int TotalRows { get; init; }
    public int ProcessedRows { get; init; }
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public int ProgressPercent => TotalRows > 0 ? (int)((double)ProcessedRows / TotalRows * 100) : 0;
    public IReadOnlyList<BulkImportRowError>? Errors { get; init; }
    public string? InitiatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
