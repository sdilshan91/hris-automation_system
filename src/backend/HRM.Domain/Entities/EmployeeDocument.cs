using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// Represents a document attached to an employee record (US-CHR-008).
/// Tenant-scoped via BaseEntity.TenantId. Supports soft-delete (IsDeleted).
/// </summary>
public sealed class EmployeeDocument : BaseEntity
{
    /// <summary>
    /// FK to the owning employee (required).
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// Original file name as uploaded by the user (max 255 chars).
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Object storage key/path (max 500 chars).
    /// Pattern: {tenantId}/core-hr/{employeeId}/{yyyy}/{mm}/{filename}
    /// </summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// MIME type of the uploaded file (max 100 chars).
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// Document category: Contract, ID, Certificate, Other (FR-1).
    /// </summary>
    public DocumentCategory Category { get; set; }

    /// <summary>
    /// Optional description of the document.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional expiry date for the document (e.g. visa, certificate expiry).
    /// </summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>
    /// User ID of the person who uploaded the document.
    /// </summary>
    public Guid UploadedBy { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    /// <summary>
    /// The employee this document belongs to.
    /// </summary>
    public Employee? Employee { get; set; }
}
