using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// Evidence/artifact file attached to a <see cref="SelfAssessmentItem"/> (one goal) — US-PRF-002 FR-5/NFR-4.
/// Stores only attachment METADATA + a tenant-scoped storage key; the bytes live behind the existing
/// <c>IFileStorage</c> abstraction. Per-goal limits (≤5 files, ≤10MB each) are enforced in the service.
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>.
/// Maps to the "self_assessment_attachment" table.
/// </summary>
public sealed class SelfAssessmentAttachment : BaseEntity
{
    /// <summary>The self-assessment item (goal) this evidence belongs to (FK, required).</summary>
    public Guid SelfAssessmentItemId { get; set; }

    /// <summary>Original uploaded file name (display only).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME content type of the uploaded file.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>File size in bytes (≤ 10MB enforced at upload, FR-5).</summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Tenant-scoped storage key/path in the document store (NFR-4). The path is namespaced by tenant +
    /// self-assessment so files never collide or leak across tenants.
    /// </summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>
    /// SEAM (NFR-4 virus scan): there is no AV scanner in the platform yet (see <c>AllowWithLogVirusScanner</c>).
    /// Until a real scanner lands this is true at acceptance and the scan is a no-op pass.
    /// TODO(virus-scan, US-SEC): gate acceptance on a real scan before flipping this to true.
    /// </summary>
    public bool IsScanned { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public SelfAssessmentItem? SelfAssessmentItem { get; set; }
}
