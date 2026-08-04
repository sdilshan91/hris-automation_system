using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A document attached to an interview — the interview guide or the evaluation criteria (US-REC-005 FR-8).
///
/// <para>The AC was deferred as CONDITIONAL on "File &amp; Document Management (S26)". <b>That rationale has
/// expired</b>: <c>IFileStorage</c>, <c>IVirusScanner</c> and the whole upload/scan/store idiom
/// <c>EmployeeDocumentService</c> uses all ship. The AC was blocked on nothing but the work itself — the same
/// expired-deferral class as US-REC-010 FR-8/FR-9.</para>
///
/// <para>A CHILD table rather than a nullable path column on <see cref="Interview"/>: the AC itself names two
/// document kinds, so one column would force an overwrite the moment a recruiter attaches both, and adding the
/// second later would mean a migration AND a data move. It also carries the per-file metadata a single column
/// cannot — uploader, size, MIME type — mirroring how <see cref="EmployeeDocument"/> already models this.</para>
/// </summary>
public sealed class InterviewAttachment : BaseEntity, IAuditExempt
{
    /// <summary>The interview this document belongs to.</summary>
    public Guid InterviewId { get; set; }

    /// <summary>Original file name as uploaded (display only — never used to build a storage path).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Tenant-relative storage path; <c>IFileStorage</c> prefixes the tenant id.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Size in bytes of the stored file, taken from the uploaded content.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>Content type, validated against the real magic bytes rather than trusted from the client.</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>Guide, evaluation criteria, or other.</summary>
    public InterviewAttachmentKind Kind { get; set; } = InterviewAttachmentKind.Guide;

    /// <summary>Optional note from the recruiter.</summary>
    public string? Description { get; set; }

    /// <summary>The user who uploaded it.</summary>
    public Guid UploadedBy { get; set; }

    // Navigation
    public Interview? Interview { get; set; }
}
