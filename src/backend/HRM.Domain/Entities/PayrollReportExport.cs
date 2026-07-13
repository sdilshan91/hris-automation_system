namespace HRM.Domain.Entities;

/// <summary>
/// ISSUE-178 PR2 (async large payroll-report export): a single CSV / Excel / PDF export of a payroll report
/// (the US-PAY-009 / US-RPT-003 reports served at <c>api/v1/payroll/reports</c>). Tenant-scoped (inherits
/// <see cref="BaseEntity"/> → EF global query filter + TenantInterceptor stamp the TenantId). One row tracks the
/// full lifecycle of an export file: Queued → Processing → Completed/Failed, then Expired (after the 7-day
/// retention window).
///
/// <para>Small exports (&lt; 1000 rows) are rendered inline and land directly in <c>Completed</c>; large exports
/// (&ge; 1000 rows) are <c>Queued</c> and rendered by the <c>PayrollReportExportJob</c> Hangfire job. The stored
/// file path, size, row count and expiry are recorded on completion so the history/download endpoints can read
/// everything from this single row.</para>
///
/// <para>This is the payroll-reports parallel of the SHIPPED US-RPT-004 <see cref="HrReportExport"/> entity — a
/// deliberate 1:1 clone. It is SEPARATE from the generic-report export (<see cref="HrReportExport"/>), the leave
/// report export, and the US-ADM-010 tenant data export — each has its own entity + table.</para>
/// </summary>
public sealed class PayrollReportExport : BaseEntity, IAuditExempt
{
    /// <summary>The user who initiated the export (drives the per-user concurrency limit + ownership check).</summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>The <c>PayrollReportType</c> name being exported (e.g. "PayrollSummary", "BankAdvice"). Stored
    /// verbatim (validated at request time), re-parsed when the async job regenerates the report.</summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary>The requested output format.</summary>
    public PayrollReportExportFormat Format { get; set; } = PayrollReportExportFormat.Csv;

    /// <summary>The resolved report filters as JSON (the full PayrollReportQueryParams, incl. the ISSUE-178 PR1
    /// salary-structure + date-range filters) — re-applied verbatim when the async job regenerates the report, and
    /// surfaced in the audit trail.</summary>
    public string FiltersJson { get; set; } = "{}";

    /// <summary>Current lifecycle state (sync vs async; async-complete notify).</summary>
    public PayrollReportExportStatus Status { get; set; } = PayrollReportExportStatus.Queued;

    /// <summary>Rows in the report (drives the sync/async routing + audit). 0 until known.</summary>
    public int RowCount { get; set; }

    /// <summary>Size of the rendered file in bytes. 0 until Completed.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>The storage locator of the rendered file (from <c>IReportExportStorage.SaveAsync</c>). Null until Completed.</summary>
    public string? FilePath { get; set; }

    /// <summary>The failure reason when Status is Failed. Null otherwise.</summary>
    public string? Error { get; set; }

    /// <summary>When the export was requested (drives the in-progress count).</summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When generation finished (Completed or Failed). Null while Queued/Processing.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>When the file is purged (CompletedAt + 7 days). Null until Completed.</summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>ISSUE-178 PR2: the supported payroll-export formats. Member order matches the Application-layer
/// <c>PayrollExportFormat</c> (Csv, Xlsx, Pdf) so the two convert by value. The wire value is the enum name.</summary>
public enum PayrollReportExportFormat
{
    Csv = 0,
    Xlsx = 1,
    Pdf = 2,
}

/// <summary>ISSUE-178 PR2: export lifecycle states (mirrors <see cref="HrReportExportStatus"/>).</summary>
public enum PayrollReportExportStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Expired = 4,
}
