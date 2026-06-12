namespace HRM.Domain.Enums;

/// <summary>
/// Status of a bulk employee import job (US-CHR-010).
/// </summary>
public enum BulkImportStatus
{
    /// <summary>Job created, waiting to be processed.</summary>
    Pending = 0,

    /// <summary>Currently processing rows.</summary>
    Processing = 1,

    /// <summary>All rows processed successfully (or partial success).</summary>
    Completed = 2,

    /// <summary>Processing failed with an unrecoverable error.</summary>
    Failed = 3,

    /// <summary>Import was cancelled by the user before processing completed.</summary>
    Cancelled = 4,
}
