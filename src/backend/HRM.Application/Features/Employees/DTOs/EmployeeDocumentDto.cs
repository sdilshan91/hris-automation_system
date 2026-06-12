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
public sealed record DocumentDownloadResult
{
    public string SignedUrl { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
}
