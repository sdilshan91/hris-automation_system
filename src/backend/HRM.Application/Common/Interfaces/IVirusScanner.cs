namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Abstraction for virus/malware scanning of uploaded files (US-CHR-001 NFR-3).
/// TODO(prod): Wire ClamAV or equivalent production scanner.
/// Default stub implementation allows all files with a warning log.
/// </summary>
public interface IVirusScanner
{
    /// <summary>
    /// Scans a file stream for malware.
    /// </summary>
    /// <param name="content">File content to scan.</param>
    /// <param name="fileName">Original file name (for logging).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Scan result indicating whether the file is clean.</returns>
    Task<VirusScanResult> ScanAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a virus scan operation.
/// </summary>
public sealed record VirusScanResult
{
    public bool IsClean { get; init; }
    public string? ThreatName { get; init; }
    public string? Details { get; init; }

    public static VirusScanResult Clean() => new() { IsClean = true };
    public static VirusScanResult Infected(string threatName, string? details = null)
        => new() { IsClean = false, ThreatName = threatName, Details = details };
}
