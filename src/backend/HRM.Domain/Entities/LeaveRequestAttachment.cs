namespace HRM.Domain.Entities;

/// <summary>
/// A real, uploaded supporting document for a leave request (US-LV-003 FR-5 / NFR-3 / ISSUE-036).
///
/// <para>Before ISSUE-036 a leave "attachment" was just a client-supplied string on the create request, so
/// the literal <c>"x.pdf"</c> satisfied a medical-certificate requirement and no bytes were ever stored.
/// This row is the metadata for an actually-uploaded, magic-byte-sniffed, virus-scanned file; the bytes
/// live behind <c>IFileStorage</c> under a tenant-scoped key.</para>
///
/// <para><see cref="LeaveRequestId"/> is NULLABLE by design: the file is uploaded BEFORE the leave request
/// exists (the employee attaches the certificate while filling the form) and is linked at create time.
/// An unlinked row is a pending upload; a row already linked to a request cannot be reused for another.</para>
///
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>.
/// Maps to the "leave_request_attachment" table.
/// </summary>
public sealed class LeaveRequestAttachment : BaseEntity
{
    /// <summary>
    /// The leave request this file supports. NULL until the request is created and the upload is claimed
    /// (see <c>LeaveRequestService.CreateAsync</c>).
    /// </summary>
    public Guid? LeaveRequestId { get; set; }

    /// <summary>Original uploaded file name (display only).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME content type of the uploaded file (PDF/JPEG/PNG only — §10).</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>File size in bytes (≤ 5MB enforced at upload, NFR-3).</summary>
    public long SizeBytes { get; set; }

    /// <summary>Tenant-scoped storage key/path in the document store; the tenant segment is added by IFileStorage.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>True once the file has passed the <c>IVirusScanner</c> check (NFR-3).</summary>
    public bool IsScanned { get; set; }

    /// <summary>
    /// The employee who uploaded the file. Ownership gate: only this employee may reference the row when
    /// creating a leave request, so an attachment id cannot be borrowed from a colleague.
    /// </summary>
    public Guid UploadedByEmployeeId { get; set; }
}
