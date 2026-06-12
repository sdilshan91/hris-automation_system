using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Stub IVirusScanner that allows all files with a warning log.
/// TODO(prod): Wire ClamAV or equivalent production malware scanner.
/// This stub exists as a real seam for DI — it is NOT a silent no-op.
/// Every scan is logged at Warning level so it is visible in dev/staging.
/// </summary>
public sealed class AllowWithLogVirusScanner : IVirusScanner
{
    private readonly ILogger<AllowWithLogVirusScanner> _logger;

    public AllowWithLogVirusScanner(ILogger<AllowWithLogVirusScanner> logger)
    {
        _logger = logger;
    }

    public Task<VirusScanResult> ScanAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Virus scan SKIPPED (stub scanner). FileName={FileName}, Size={Size} bytes. " +
            "TODO(prod): Wire ClamAV for real malware scanning.",
            fileName, content.Length);

        return Task.FromResult(VirusScanResult.Clean());
    }
}
