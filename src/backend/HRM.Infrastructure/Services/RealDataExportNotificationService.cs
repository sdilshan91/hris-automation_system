using System.Text.Json;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-006 Phase 4: the real <see cref="IDataExportNotificationService"/> (US-ADM-010 AC-2/BR-6). Replaces the
/// log-only stub — dispatches the "your export bundle is ready" email (with the download link) to each recipient
/// via <see cref="INotificationDispatcher"/>. Recipients are RAW email addresses (the requester + the tenant's
/// billing/contact address) that may have no <c>User</c> row, so every dispatch uses the email-only
/// <see cref="NotificationRequest.RecipientEmail"/> leg (no in-app leg).
///
/// <para><b>Never throws</b> — a delivery failure must not break the export-generation pipeline (the bundle is
/// already stored + the request marked Completed); every recipient is wrapped and exceptions are swallowed + logged,
/// mirroring the LogOnly contract.</para>
/// </summary>
public sealed class RealDataExportNotificationService : IDataExportNotificationService
{
    private const string EventKey = "data_export_ready";
    private const string NotificationType = "data_export.ready";

    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<RealDataExportNotificationService> _logger;

    public RealDataExportNotificationService(
        INotificationDispatcher dispatcher, ILogger<RealDataExportNotificationService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task SendExportReadyAsync(
        Guid tenantId,
        Guid exportRequestId,
        IReadOnlyList<string> recipients,
        string downloadUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var distinct = recipients
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinct.Count == 0)
            {
                _logger.LogWarning(
                    "RealDataExportNotificationService: no recipients for export {ExportId} (tenant {TenantId}); " +
                    "download-link email not delivered.", exportRequestId, tenantId);
                return;
            }

            var payloadJson = BuildPayload(downloadUrl);

            foreach (var email in distinct)
            {
                var request = new NotificationRequest(
                    TenantId: tenantId,
                    EventKey: EventKey,
                    PayloadJson: payloadJson,
                    RecipientEmail: email,          // raw address — may have no User row (email-only leg).
                    NotificationType: NotificationType);

                // Email-only (raw addresses); the dispatcher's email leg is internally never-throw, still guarded.
                await _dispatcher.SendEmailAsync(request, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Never throw — the export is already completed; a notification failure must not surface to the pipeline.
            _logger.LogError(ex,
                "RealDataExportNotificationService: failed to dispatch export-ready email for export {ExportId} " +
                "(tenant {TenantId}).", exportRequestId, tenantId);
        }
    }

    private static string BuildPayload(string downloadUrl) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = "Your data export is ready",
            ["message"] = "Your requested data export bundle is ready to download.",
            ["export"] = new Dictionary<string, object?> { ["downloadUrl"] = downloadUrl },
        });
}
