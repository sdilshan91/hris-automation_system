namespace HRM.Infrastructure.Services;

/// <summary>
/// Configuration for the pluggable <see cref="ClamAvVirusScanner"/> (ISSUE-101). Bound from the
/// <c>VirusScanning:ClamAv</c> configuration section. The scanner is only registered when <see cref="Host"/>
/// is non-empty (see <c>DependencyInjection.AddInfrastructure</c>); otherwise the platform keeps the existing
/// <see cref="AllowWithLogVirusScanner"/> allow-stub, so nothing changes for tenants/tests that do not
/// configure ClamAV.
/// </summary>
public sealed class ClamAvOptions
{
    public const string SectionName = "VirusScanning:ClamAv";

    /// <summary>Hostname / IP of the ClamAV daemon (clamd). Empty ⇒ scanner not registered.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>TCP port of the ClamAV daemon. clamd's default is 3310.</summary>
    public int Port { get; set; } = 3310;

    /// <summary>Max seconds to wait for the TCP connection to clamd.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>Max seconds for the whole INSTREAM scan (send + response).</summary>
    public int ScanTimeoutSeconds { get; set; } = 60;

    /// <summary>INSTREAM chunk size in bytes. Must stay below clamd's <c>StreamMaxLength</c>.</summary>
    public int ChunkSize { get; set; } = 8192;

    /// <summary>
    /// Behaviour when the daemon is unreachable / the scan errors out. Default <c>false</c> = FAIL CLOSED:
    /// an un-scannable upload is rejected (surfaced as a non-clean <see cref="Application.Common.Interfaces.VirusScanResult"/>
    /// so callers reject it with a 400, exactly like a real detection). Set <c>true</c> to FAIL OPEN
    /// (allow the upload with a warning log) for environments that prefer availability over strict scanning.
    /// </summary>
    public bool FailOpen { get; set; }
}
