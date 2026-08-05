// ============================================================================
// Wave 5 readiness (ISSUE-101) — what happens when clamd is NOT there.
//
// ClamAvVirusScannerIntegrationTests already proves detection works against a real daemon: EICAR is caught,
// clean content passes. Both of those arms require the daemon to be UP.
//
// The property that actually matters when enabling ClamAV in production is the opposite one: when clamd dies,
// is unreachable, or times out, uploads must be REJECTED, not waved through. Nothing tested that. A regression
// there is invisible — every existing test still passes, prod still returns 200s, and malware uploads stop
// being scanned at all. The failure is silent by construction, which is exactly why it needs its own arms.
//
// These point the scanner at a closed port, so they need no container and run in milliseconds.
// ============================================================================

using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRM.Tests.Unit;

public sealed class ClamAvUnavailableFailClosedTests
{
    /// <summary>
    /// Binds a port, reads its number, then releases it — giving a port number very likely to refuse
    /// connections. Far faster and more deterministic than waiting for a connect timeout to a black-hole host.
    /// </summary>
    private static int UnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static ClamAvVirusScanner ScannerPointedAtNothing(bool failOpen)
    {
        var options = new ClamAvOptions
        {
            Host = "127.0.0.1",
            Port = UnusedPort(),
            ConnectTimeoutSeconds = 1,
            ScanTimeoutSeconds = 2,
            FailOpen = failOpen,
        };

        return new ClamAvVirusScanner(
            Options.Create(options), NullLogger<ClamAvVirusScanner>.Instance);
    }

    private static MemoryStream Upload() =>
        new(Encoding.UTF8.GetBytes("a perfectly ordinary looking payload"), writable: false);

    [Fact]
    public async Task An_unreachable_daemon_REJECTS_the_upload_by_default_ISSUE101()
    {
        // THE arm. This is the whole reason FailOpen defaults to false: a scanner that cannot scan must not
        // report "clean". If this ever returns Clean, uploads silently stop being scanned in production and
        // every other virus-scanning test still passes.
        var result = await ScannerPointedAtNothing(failOpen: false).ScanAsync(Upload(), "resume.pdf");

        result.IsClean.Should().BeFalse(
            "an un-scannable upload must be rejected exactly like a real detection");
        result.ThreatName.Should().Be("scan-unavailable",
            "the reason must be distinguishable from a genuine malware hit for triage");
    }

    [Fact]
    public async Task Fail_OPEN_is_opt_in_and_allows_the_upload_ISSUE101()
    {
        // The availability-over-strictness escape hatch must still work — otherwise operators who deliberately
        // chose it would find uploads blocked with no way back short of a deploy.
        var result = await ScannerPointedAtNothing(failOpen: true).ScanAsync(Upload(), "resume.pdf");

        result.IsClean.Should().BeTrue("FailOpen=true is an explicit choice to allow un-scanned uploads");
    }

    [Fact]
    public async Task A_daemon_outage_never_surfaces_as_a_500_ISSUE101()
    {
        // A socket failure escaping as an exception would turn "the AV box is down" into "the whole upload
        // endpoint is throwing", which reads as an app bug and buries the real cause.
        var act = async () => await ScannerPointedAtNothing(failOpen: false).ScanAsync(Upload(), "resume.pdf");

        await act.Should().NotThrowAsync(
            "the daemon being down is a scan verdict, not an unhandled error");
    }

    [Fact]
    public async Task A_genuine_caller_cancellation_still_propagates_ISSUE101()
    {
        // Deliberately NOT folded into the fail policy: if the client aborted the request, treating it as a
        // malware detection would attribute an infection to a file nobody finished uploading.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await ScannerPointedAtNothing(failOpen: false)
            .ScanAsync(Upload(), "resume.pdf", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "a caller abort must stay an abort, not become a fake detection");
    }
}
