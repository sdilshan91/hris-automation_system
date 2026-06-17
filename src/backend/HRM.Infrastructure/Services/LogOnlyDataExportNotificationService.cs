using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IDataExportNotificationService"/> (US-ADM-010 AC-2/BR-6). Logs the download-link email it
/// WOULD send to the requester + the tenant's billing contact, treating that as a successful send — mirroring the
/// platform's other log-only notification seams. Real transactional email + a pre-signed/time-limited S3 URL are
/// DEFERRED (TODO US-NTF); wiring a real sender is a one-class swap.
/// </summary>
public sealed class LogOnlyDataExportNotificationService : IDataExportNotificationService
{
    private readonly ILogger<LogOnlyDataExportNotificationService> _logger;

    public LogOnlyDataExportNotificationService(ILogger<LogOnlyDataExportNotificationService> logger)
        => _logger = logger;

    public Task SendExportReadyAsync(
        Guid tenantId,
        Guid exportRequestId,
        IReadOnlyList<string> recipients,
        string downloadUrl,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[DATA-EXPORT-EMAIL-STUB] Would email export {ExportId} (tenant {TenantId}) download link to {Recipients}. " +
            "Download URL: {DownloadUrl}. Configure a real mail sender to enable delivery.",
            exportRequestId, tenantId, string.Join(", ", recipients), downloadUrl);

        return Task.CompletedTask;
    }
}
