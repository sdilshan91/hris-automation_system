namespace HRM.Domain.Entities;

/// <summary>
/// US-ADM-010: a tenant data-export request (GDPR Article 20 data portability + termination-grace extraction).
/// Tenant-scoped (inherits <see cref="BaseEntity"/> → EF global query filter + TenantInterceptor stamp the
/// TenantId). One row tracks the full lifecycle of an export bundle: Queued → Processing → Completed/Failed,
/// then Expired (after the 72h download window). The generated bundle path, manifest, row count and expiry are
/// recorded on completion so the history/status/download endpoints can read everything from this single row.
/// </summary>
public sealed class ExportRequest : BaseEntity
{
    /// <summary>
    /// The requested scope: the literal "full" (every exportable entity) OR a comma-separated list of entity
    /// codes (e.g. "Employees,LeaveTypes") for a partial export (FR-1). Stored as a plain string; the
    /// generation service parses it against the curated entity registry.
    /// </summary>
    public string Scope { get; set; } = "full";

    /// <summary>Current lifecycle state (FR-9 in-progress status).</summary>
    public ExportRequestStatus Status { get; set; } = ExportRequestStatus.Queued;

    /// <summary>The user who initiated the export (the Tenant Admin, or the System Admin actor for AC-6).</summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>When the export was requested (drives the calendar-month rate limit + the +72h expiry).</summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When generation finished (Completed or Failed). Null while Queued/Processing.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>The storage-relative path of the generated ZIP bundle (FilePath). Null until Completed.</summary>
    public string? FilePath { get; set; }

    /// <summary>When the download link expires (RequestedAt-independent: CompletedAt + 72h). Null until Completed.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>The manifest.json content captured on completion (FR-6) for quick history/status reads.</summary>
    public string? ManifestJson { get; set; }

    /// <summary>Total rows exported across all entity CSVs (FR-6 summary). Null until Completed.</summary>
    public int? RowCountTotal { get; set; }

    /// <summary>The failure reason when Status is Failed. Null otherwise.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// True when the requester is a System Admin operating cross-tenant (AC-6) — recorded so the audit/history
    /// can attribute the actor. The ordinary Tenant-Admin path leaves this false.
    /// </summary>
    public bool InitiatedBySystemAdmin { get; set; }
}

/// <summary>US-ADM-010: data-export lifecycle states.</summary>
public enum ExportRequestStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Expired = 4,
}
