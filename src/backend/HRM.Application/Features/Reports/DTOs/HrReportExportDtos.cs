namespace HRM.Application.Features.Reports.DTOs;

/// <summary>
/// US-RPT-004: the request body for <c>POST /api/v1/reports/{type}/export</c>. <c>{type}</c> (the kebab report
/// key) comes from the route; this carries the format, the §7 report filters, and the optional include-charts flag.
/// </summary>
public sealed record HrReportExportRequest
{
    /// <summary>The output format: "csv" | "xlsx" | "pdf" (case-insensitive). Validated by the service.</summary>
    public string Format { get; init; } = "csv";

    /// <summary>The same filters used to generate the report (§7). Null = report defaults.</summary>
    public HrReportQueryParams? Filters { get; init; }

    /// <summary>FR-4: include charts in the export (default true). Currently advisory — chart-image embedding in
    /// the PDF is deferred (no server-side chart renderer); the flag is persisted/echoed for forward compatibility.</summary>
    public bool IncludeCharts { get; init; } = true;
}

/// <summary>
/// US-RPT-004: the uniform JSON response for the export-initiation endpoint (NOT the file — the file is served by
/// the separate /download endpoint). <c>Status</c> is "Completed" for a synchronous (&lt; 1000-row) export and
/// "Queued" for an asynchronous one (FR-5). <c>Format</c> echoes the requested lower-case format.
/// </summary>
public sealed record HrReportExportInitiatedDto
{
    public Guid ExportId { get; init; }
    public string Status { get; init; } = string.Empty;
    public int RowCount { get; init; }
    public string Format { get; init; } = string.Empty;
}

/// <summary>US-RPT-004: one row in the current user's export history (the <c>GET /api/v1/reports/exports</c> list).</summary>
public sealed record HrReportExportListItemDto
{
    public Guid Id { get; init; }
    public string ReportType { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int RowCount { get; init; }
    public long FileSizeBytes { get; init; }
    public DateTime? ExpiresAt { get; init; }

    /// <summary>True when the file is Completed AND still within its 7-day retention window (BR-3). The FE shows a
    /// download button only when this is true.</summary>
    public bool DownloadReady { get; init; }
}

/// <summary>
/// US-RPT-004: the bytes + metadata returned by <c>GetForDownloadAsync</c> for the /download endpoint (which then
/// emits a <c>FileContentResult</c>). <see cref="Expired"/> distinguishes a purged/expired file (controller → 410)
/// from a missing/cross-tenant one (controller → 404), so the contract stays explicit.
/// </summary>
public sealed record HrReportExportDownloadDto
{
    public byte[] Content { get; init; } = [];
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;

    /// <summary>True when the export exists + is owned by the caller but has expired/been purged (→ HTTP 410).</summary>
    public bool Expired { get; init; }

    public static HrReportExportDownloadDto File(byte[] content, string fileName, string contentType) =>
        new() { Content = content, FileName = fileName, ContentType = contentType };

    public static HrReportExportDownloadDto ExpiredResult() => new() { Expired = true };
}
