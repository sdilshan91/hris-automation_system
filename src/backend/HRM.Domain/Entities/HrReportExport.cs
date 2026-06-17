namespace HRM.Domain.Entities;

/// <summary>
/// US-RPT-004: a single CSV / Excel / PDF export of a generic HR / leave / attendance report (the US-RPT-001 /
/// US-RPT-002 reports served at <c>api/v1/reports</c>). Tenant-scoped (inherits <see cref="BaseEntity"/> → EF
/// global query filter + TenantInterceptor stamp the TenantId). One row tracks the full lifecycle of an export
/// file: Queued → Processing → Completed/Failed, then Expired (after the 7-day retention window, BR-3).
///
/// <para>Small exports (&lt; 1000 rows, FR-5) are rendered inline and land directly in <c>Completed</c>; large
/// exports (&ge; 1000 rows) are <c>Queued</c> and rendered by the <c>HrReportExportJob</c> Hangfire job. The
/// stored file path, size, row count and expiry are recorded on completion so the history/download endpoints can
/// read everything from this single row.</para>
///
/// <para>NOTE: this is SEPARATE from payroll exports (US-PAY-009) and the US-ADM-010 tenant data export — those
/// have their own entities. This row covers ONLY the generic reports surface.</para>
/// </summary>
public sealed class HrReportExport : BaseEntity
{
    /// <summary>The user who initiated the export (drives the FR-10 per-user concurrency limit + ownership check).</summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>The kebab report-type key being exported (e.g. "headcount", "leave-utilization"). Free string —
    /// validated against <c>HrReportTypeKey</c> at request time, stored verbatim for the history/audit trail.</summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary>The requested output format (FR-1).</summary>
    public HrReportExportFormat Format { get; set; } = HrReportExportFormat.Csv;

    /// <summary>The resolved report filters as JSON (the §7 HrReportQueryParams) — re-applied verbatim when the
    /// async job regenerates the report, and surfaced in the FR-9 audit trail.</summary>
    public string FiltersJson { get; set; } = "{}";

    /// <summary>Current lifecycle state (FR-5 sync vs async; FR-8 async-complete notify).</summary>
    public HrReportExportStatus Status { get; set; } = HrReportExportStatus.Queued;

    /// <summary>Rows in the report table (FR-9 audit; drives the FR-5 sync/async routing). 0 until known.</summary>
    public int RowCount { get; set; }

    /// <summary>Size of the rendered file in bytes. 0 until Completed.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>The storage locator of the rendered file (from <c>IReportExportStorage.SaveAsync</c>). Null until Completed.</summary>
    public string? FilePath { get; set; }

    /// <summary>The failure reason when Status is Failed. Null otherwise.</summary>
    public string? Error { get; set; }

    /// <summary>When the export was requested (drives the FR-10 in-progress count).</summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When generation finished (Completed or Failed). Null while Queued/Processing.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>When the file is purged (BR-3: CompletedAt + 7 days). Null until Completed.</summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>US-RPT-004: the supported export formats (FR-1). The wire value is the lower-case enum name.</summary>
public enum HrReportExportFormat
{
    Csv = 0,
    Xlsx = 1,
    Pdf = 2,
}

/// <summary>US-RPT-004: export lifecycle states (mirrors the US-ADM-010 ExportRequestStatus shape).</summary>
public enum HrReportExportStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Expired = 4,
}
