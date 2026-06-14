using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Local / log-only implementation of the report-export storage seam (US-LV-012 FR-5).
///
/// Writes the export file to a tenant-scoped path under the OS temp directory
/// (<c>{temp}/hrm-report-exports/{tenantId}/reports/leave/{reportId}.{ext}</c>) and logs the location,
/// standing in for the real tenant-scoped blob store until one exists. The path layout deliberately
/// mirrors the story's <c>{tenantId}/reports/leave/{reportId}.xlsx</c> contract (§7) so swapping in a
/// blob provider is a drop-in replacement.
///
/// TODO(blob-storage): replace with a real tenant-scoped blob store + signed download URL + 30-day
///   retention purge (§10), and a real notification dispatch in place of the log line.
/// </summary>
public sealed class LocalReportExportStorage : IReportExportStorage
{
    private readonly ILogger<LocalReportExportStorage> _logger;

    public LocalReportExportStorage(ILogger<LocalReportExportStorage> logger)
    {
        _logger = logger;
    }

    public async Task<string> SaveAsync(
        Guid tenantId,
        Guid reportId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        // Tenant-scoped directory mirroring the {tenantId}/reports/leave/{reportId}.{ext} blob layout.
        var directory = Path.Combine(
            Path.GetTempPath(), "hrm-report-exports", tenantId.ToString(), "reports", "leave");
        Directory.CreateDirectory(directory);

        var fullPath = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(fullPath, content, cancellationToken);

        _logger.LogInformation(
            "Report export {ReportId} ({Bytes} bytes, {ContentType}) stored for tenant {TenantId} at {Path}. " +
            "Action {AuditAction}. TODO(blob-storage): move to real tenant-scoped blob store.",
            reportId, content.Length, contentType, tenantId, fullPath, "Leave.ReportExported");

        return fullPath;
    }
}
