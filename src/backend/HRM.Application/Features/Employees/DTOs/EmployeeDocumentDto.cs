namespace HRM.Application.Features.Employees.DTOs;

/// <summary>
/// DTO for employee document metadata (US-CHR-008 FR-9).
/// </summary>
public sealed record EmployeeDocumentDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public string MimeType { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public Guid UploadedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>
    /// BUG-114 (TC-CHR-205): set on an upload response when the tenant's cumulative document storage has
    /// reached ≥80% of the plan's <c>MaxStorageGb</c> after this upload — a soft warning before the hard
    /// block at 100%. Null when under 80% or when the plan has no storage limit (unlimited).
    /// </summary>
    public string? StorageWarning { get; init; }
}

/// <summary>
/// Paginated list result for employee document queries.
/// </summary>
public sealed record EmployeeDocumentListResult
{
    public IReadOnlyList<EmployeeDocumentDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
}

/// <summary>
/// Request body for uploading an employee document (US-CHR-008 FR-1).
/// Category and optional metadata accompany the multipart file upload.
/// </summary>
public sealed record UploadEmployeeDocumentRequest
{
    public string Category { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime? ExpiryDate { get; init; }
}

/// <summary>
/// Response for document download containing a signed URL (US-CHR-008 FR-6, AC-4).
/// </summary>
/// <summary>
/// GAP-027 — the document's actual BYTES, streamed from an authenticated endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the signed-URL shape for delivery. <c>LocalFileStorage.GetSignedUrl</c> fabricates
/// <c>/files/{tenantId}/{path}</c> — a URL scheme NO route has ever served — and its own comment admits the
/// production pre-signing "would" be implemented. It never was. The frontend set that string as an anchor
/// href, so every Download click navigated to a 404.
/// </para>
/// <para>
/// Streaming matches what every other download endpoint in this codebase already does — payslips, data
/// exports and HR report exports all <c>return File(...)</c> and none issue signed URLs. It is also
/// genuinely authenticated: a bare <c>/files/...</c> navigation cannot carry a bearer token, which is the
/// reason real deployments use pre-signed URLs and the reason a half-built scheme is worse than none.
/// </para>
/// </remarks>
public sealed record DocumentContentResult(
    byte[] Content,
    string ContentType,
    string FileName);

public sealed record DocumentDownloadResult
{
    public string SignedUrl { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
}
