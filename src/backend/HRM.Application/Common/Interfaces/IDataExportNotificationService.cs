namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-010 (AC-2/BR-6): the seam that dispatches the export download-link email to the requester AND the
/// tenant's billing contact. Real email + a pre-signed/time-limited S3 URL are DEFERRED (TODO US-NTF) — the
/// log-only implementation records the recipients + the local download path/stub token so the flow is complete
/// and testable without a mail server.
/// </summary>
public interface IDataExportNotificationService
{
    /// <summary>
    /// Notifies the recipients that the export bundle is ready, with the (stub) download link. Best-effort —
    /// implementations never throw into the generation pipeline.
    /// </summary>
    Task SendExportReadyAsync(
        Guid tenantId,
        Guid exportRequestId,
        IReadOnlyList<string> recipients,
        string downloadUrl,
        CancellationToken cancellationToken = default);
}
