namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>
/// ISSUE-178 PR2: the request body for <c>POST /api/v1/payroll/reports/{reportType}/export</c>. <c>{reportType}</c>
/// (the PayrollReportType name) comes from the route; this carries the format + the report filters (incl. the
/// PR1 salary-structure/date-range filters).
/// </summary>
public sealed record PayrollReportExportRequest
{
    /// <summary>The output format: "csv" | "xlsx" | "pdf" (case-insensitive). Validated by the service.</summary>
    public string Format { get; init; } = "csv";

    /// <summary>The same filters used to generate the report (FR-3 + PR1). Null = report defaults.</summary>
    public PayrollReportQueryParams? Filters { get; init; }
}

/// <summary>
/// ISSUE-178 PR2: the uniform JSON response for the export-initiation endpoint (NOT the file — the file is served
/// by the separate /download endpoint). <c>Status</c> is "Completed" for a synchronous (&lt; 1000-row) export and
/// "Queued" for an asynchronous one. <c>Format</c> echoes the requested lower-case format.
/// </summary>
public sealed record PayrollReportExportInitiatedDto
{
    public Guid ExportId { get; init; }
    public string Status { get; init; } = string.Empty;
    public int RowCount { get; init; }
    public string Format { get; init; } = string.Empty;
}

/// <summary>ISSUE-178 PR2: one row in the current user's export history (<c>GET /api/v1/payroll/reports/exports</c>).</summary>
public sealed record PayrollReportExportListItemDto
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

    /// <summary>True when the file is Completed AND still within its 7-day retention window. The FE shows a
    /// download button only when this is true.</summary>
    public bool DownloadReady { get; init; }
}

/// <summary>
/// ISSUE-178 PR2: the bytes + metadata returned by <c>GetForDownloadAsync</c> for the /download endpoint (which
/// then emits a <c>FileContentResult</c>). <see cref="Expired"/> distinguishes a purged/expired file
/// (controller → 410) from a missing/cross-tenant one (controller → 404), so the contract stays explicit.
/// </summary>
public sealed record PayrollReportExportDownloadDto
{
    public byte[] Content { get; init; } = [];
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;

    /// <summary>True when the export exists + is owned by the caller but has expired/been purged (→ HTTP 410).</summary>
    public bool Expired { get; init; }

    public static PayrollReportExportDownloadDto File(byte[] content, string fileName, string contentType) =>
        new() { Content = content, FileName = fileName, ContentType = contentType };

    public static PayrollReportExportDownloadDto ExpiredResult() => new() { Expired = true };
}
