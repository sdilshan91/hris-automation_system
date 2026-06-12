using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// Tracks bulk employee import operations (US-CHR-010).
/// Used for both sync (status set to Completed immediately) and async (Hangfire) imports.
/// </summary>
public sealed class BulkImportJob : BaseEntity
{
    /// <summary>
    /// Original uploaded file name.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Total number of data rows in the file (excluding header).
    /// </summary>
    public int TotalRows { get; set; }

    /// <summary>
    /// Number of rows successfully imported.
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of rows that failed validation.
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Current processing status.
    /// </summary>
    public BulkImportStatus Status { get; set; } = BulkImportStatus.Pending;

    /// <summary>
    /// Serialized JSON array of per-row error details.
    /// Each entry: { rowNumber, field, error }.
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// User who initiated the import.
    /// </summary>
    public string? InitiatedBy { get; set; }

    /// <summary>
    /// Timestamp when processing started.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Timestamp when processing completed (success or failure).
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Number of rows processed so far (for progress tracking).
    /// </summary>
    public int ProcessedRows { get; set; }

    /// <summary>
    /// Hangfire job ID for async imports.
    /// </summary>
    public string? HangfireJobId { get; set; }
}
