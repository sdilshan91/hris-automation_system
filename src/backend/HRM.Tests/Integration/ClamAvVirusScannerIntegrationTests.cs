// ============================================================================
// ISSUE-101 / NFR-3 — ClamAvVirusScanner against a REAL clamd daemon (Testcontainers).
//
// The repo's only ClamAV coverage was the transport-parser unit test (ClamAvInStreamProtocolTests) —
// nothing exercised a live clamd or proved EICAR is actually detected end-to-end. This spins the real
// clamav/clamav container, points the production ClamAvVirusScanner at it, and asserts:
//   - the EICAR standard test string is flagged INFECTED (with the daemon's threat name), and
//   - a clean stream passes.
//
// CATEGORY-GATED (`[Trait("Category","ClamAv")]`) + opt-in: the clamav image needs ~2 GB RAM and a
// first-boot signature-DB load, so it is EXCLUDED from the default test run. Execute explicitly:
//   dotnet test --filter Category=ClamAv
// ============================================================================

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace HRM.Tests.Integration;

[Trait("Category", "ClamAv")]
public sealed class ClamAvVirusScannerIntegrationTests : IAsyncLifetime
{
    // The EICAR standard anti-virus test string (harmless; every scanner flags it). Split so this source
    // file itself is not flagged by a scanner.
    private const string Eicar =
        @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    private readonly IContainer _clamav = new ContainerBuilder("clamav/clamav:stable")
        .WithPortBinding(3310, true)
        // The clamav image ships a HEALTHCHECK (clamdcheck.sh) that goes healthy once clamd has loaded its
        // signature DB and accepts connections — the correct readiness signal (port-open alone is too early).
        .WithWaitStrategy(Wait.ForUnixContainer().UntilContainerIsHealthy())
        .Build();

    public async Task InitializeAsync() => await _clamav.StartAsync();
    public async Task DisposeAsync() => await _clamav.DisposeAsync();

    private ClamAvVirusScanner Scanner() => new(
        Options.Create(new ClamAvOptions
        {
            Host = _clamav.Hostname,
            Port = _clamav.GetMappedPublicPort(3310),
            ConnectTimeoutSeconds = 30,
            ScanTimeoutSeconds = 120,
            FailOpen = false,
        }),
        NullLogger<ClamAvVirusScanner>.Instance);

    [Fact]
    [Trait("TC", "TC-CHR-001-101")]
    public async Task Scan_EicarTestString_IsDetectedInfected()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(Eicar));

        var result = await Scanner().ScanAsync(stream, "eicar.com");

        result.IsClean.Should().BeFalse("the EICAR test string must be detected by a real clamd");
        result.ThreatName.Should().NotBeNullOrWhiteSpace();
        result.ThreatName!.ToLowerInvariant().Should().Contain("eicar"); // e.g. Win.Test.EICAR_HDB-1
    }

    [Fact]
    [Trait("TC", "TC-CHR-001-101")]
    public async Task Scan_CleanContent_PassesAsClean()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("This is an ordinary, harmless document body."));

        var result = await Scanner().ScanAsync(stream, "clean.txt");

        result.IsClean.Should().BeTrue();
        result.ThreatName.Should().BeNull();
    }
}
