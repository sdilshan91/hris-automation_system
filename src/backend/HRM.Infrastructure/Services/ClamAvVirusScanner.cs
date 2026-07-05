using System.Net.Sockets;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Production <see cref="IVirusScanner"/> backed by a ClamAV daemon (clamd) over TCP using the INSTREAM
/// protocol (ISSUE-101). Only registered when <c>VirusScanning:ClamAv:Host</c> is configured; otherwise the
/// platform keeps the <see cref="AllowWithLogVirusScanner"/> allow-stub, so existing behaviour/tests are
/// unchanged.
///
/// Outcomes (matching the existing <see cref="VirusScanResult"/> contract the callers already react to —
/// e.g. <c>ApplicantService</c> / <c>AssetService</c> reject any non-clean result with a 400):
/// <list type="bullet">
/// <item>clean file ⇒ <see cref="VirusScanResult.Clean"/>.</item>
/// <item>detected signature ⇒ <see cref="VirusScanResult.Infected"/> (upload rejected).</item>
/// <item>daemon unreachable / scan error ⇒ <b>fail closed by default</b> — a non-clean
/// <see cref="VirusScanResult"/> so the upload is rejected. Set <c>VirusScanning:ClamAv:FailOpen=true</c> to
/// allow-with-warning instead. A genuine caller-cancellation is re-thrown (never masked as a detection).</item>
/// </list>
/// The wire framing + response parsing live in the transport-agnostic <see cref="ClamAvInStreamProtocol"/> so
/// they are unit-testable without a live daemon. Uses only the BCL socket stack (no NuGet ClamAV client).
/// </summary>
public sealed class ClamAvVirusScanner : IVirusScanner
{
    private readonly ClamAvOptions _options;
    private readonly ILogger<ClamAvVirusScanner> _logger;

    public ClamAvVirusScanner(IOptions<ClamAvOptions> options, ILogger<ClamAvVirusScanner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<VirusScanResult> ScanAsync(
        Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        // Bound the whole scan (connect + send + response) so a hung daemon never hangs the request. The
        // linked token still honours a genuine caller cancellation.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ScanTimeoutSeconds)));
        var token = timeoutCts.Token;

        try
        {
            using var client = new TcpClient();

            // ConnectAsync gets its own (shorter) budget so an unreachable host fails fast.
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                connectCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ConnectTimeoutSeconds)));
                await client.ConnectAsync(_options.Host, _options.Port, connectCts.Token);
            }

            await using var network = client.GetStream();

            if (content.CanSeek)
                content.Position = 0; // defensive: scan the whole file from the start.

            await ClamAvInStreamProtocol.WriteInStreamAsync(network, content, _options.ChunkSize, token);
            var response = await ClamAvInStreamProtocol.ReadResponseAsync(network, token);
            var result = ClamAvInStreamProtocol.ParseResponse(response);

            if (result.IsClean)
                _logger.LogInformation(
                    "ClamAV scan clean. FileName={FileName}, Host={Host}:{Port}", fileName, _options.Host, _options.Port);
            else
                _logger.LogWarning(
                    "ClamAV detected malware. FileName={FileName}, Threat={Threat}", fileName, result.ThreatName);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine caller cancellation (request aborted) — propagate, do NOT treat as a detection.
            throw;
        }
        catch (Exception ex)
        {
            // Socket failure, connect/scan timeout, or a clamd ERROR reply. Apply the fail policy.
            return HandleScanFailure(ex, fileName);
        }
    }

    /// <summary>
    /// Fail-closed (default): return a non-clean result so the upload is rejected exactly like a real
    /// detection. Fail-open (opt-in): allow with a warning log. The daemon being down never surfaces as a 500.
    /// </summary>
    private VirusScanResult HandleScanFailure(Exception ex, string fileName)
    {
        if (_options.FailOpen)
        {
            _logger.LogWarning(ex,
                "ClamAV unavailable — FAILING OPEN (allowing upload). FileName={FileName}, Host={Host}:{Port}",
                fileName, _options.Host, _options.Port);
            return VirusScanResult.Clean();
        }

        _logger.LogError(ex,
            "ClamAV unavailable — FAILING CLOSED (rejecting upload). FileName={FileName}, Host={Host}:{Port}",
            fileName, _options.Host, _options.Port);
        return VirusScanResult.Infected(
            "scan-unavailable",
            "The malware scanner could not be reached; the upload was rejected (fail-closed).");
    }
}
