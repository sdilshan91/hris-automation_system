// ============================================================================
// ISSUE-101 — ClamAV INSTREAM scanner (config-gated).
//
// Two independently-testable seams are exercised here WITHOUT a live clamd daemon:
//   1. ClamAvInStreamProtocol — the pure, transport-agnostic wire framing +
//      response parsing (extracted from ClamAvVirusScanner precisely so it can be
//      unit-tested against an in-memory Stream). The framing/parse assertions are
//      EXACT byte / EXACT string comparisons, so they are self-evidently meaningful:
//      they pin the ClamAV wire contract (zINSTREAM\0 + big-endian length-prefixed
//      chunks + zero-length terminator; "... OK" / "{sig} FOUND" / "... ERROR").
//   2. The DI gate in DependencyInjection.AddInfrastructure — a registration guard
//      proving the scanner is only wired when VirusScanning:ClamAv:Host is set,
//      otherwise the AllowWithLogVirusScanner allow-stub is kept. No socket is
//      opened (the ctor stores options+logger; the socket only lives in ScanAsync).
//
// ClamAvInStreamProtocol / ClamAvProtocolException are `internal` in HRM.Infrastructure;
// this test project has [InternalsVisibleTo("HRM.Tests")] so it can reach them directly.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Unit;

public sealed class ClamAvInStreamProtocolTests
{
    // ── Framing: command + big-endian length-prefixed chunks + terminator ──

    [Fact]
    public async Task ClamAvInStream_FramesChunksAndTerminator_ISSUE101()
    {
        // 600-byte payload with a 256-byte chunk size FORCES THREE chunks (256 + 256 + 88),
        // which proves the length prefix + payload framing repeats per chunk. Using a chunk
        // length of 256 also makes the byte ORDER observable: 256 big-endian is
        // [0x00, 0x00, 0x01, 0x00]; a little-endian writer would emit [0x00, 0x01, 0x00, 0x00].
        // So this asserts BIG-endian, not merely "a 4-byte prefix".
        const int chunkSize = 256;
        var payload = new byte[600];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i % 251); // distinct-ish, deterministic pattern

        using var target = new MemoryStream();
        using var content = new MemoryStream(payload);

        await ClamAvInStreamProtocol.WriteInStreamAsync(target, content, chunkSize, CancellationToken.None);

        // Build the EXACT expected wire bytes independently of the implementation. The length
        // prefixes are hand-written big-endian byte arrays (NOT via BinaryPrimitives) so the
        // test does not merely mirror the production encoding call.
        using var expected = new MemoryStream();
        expected.Write(Encoding.ASCII.GetBytes("zINSTREAM\0"));   // 1. NUL-terminated command
        expected.Write([0x00, 0x00, 0x01, 0x00]);                 // 2a. len 256 (big-endian)
        expected.Write(payload, 0, 256);
        expected.Write([0x00, 0x00, 0x01, 0x00]);                 // 2b. len 256 (big-endian)
        expected.Write(payload, 256, 256);
        expected.Write([0x00, 0x00, 0x00, 0x58]);                 // 2c. len 88 = 0x58 (big-endian)
        expected.Write(payload, 512, 88);
        expected.Write([0x00, 0x00, 0x00, 0x00]);                 // 3. zero-length terminator

        target.ToArray().Should().Equal(expected.ToArray());
    }

    // ── Response parsing: OK / FOUND / ERROR ──────────────────────────────

    [Fact]
    public void ClamAvParse_CleanResponse_Clean_ISSUE101()
    {
        var result = ClamAvInStreamProtocol.ParseResponse("stream: OK");

        result.IsClean.Should().BeTrue();
        result.ThreatName.Should().BeNull();
    }

    [Fact]
    public void ClamAvParse_CleanResponseWithNulTerminator_Clean_ISSUE101()
    {
        // A real clamd reply is NUL-terminated ("stream: OK\0"). ParseResponse must strip the
        // NUL and still read Clean — this proves the raw-from-the-socket form parses.
        var result = ClamAvInStreamProtocol.ParseResponse("stream: OK\0");

        result.IsClean.Should().BeTrue();
    }

    [Fact]
    public void ClamAvParse_FoundResponse_Infected_ISSUE101()
    {
        var result = ClamAvInStreamProtocol.ParseResponse("stream: Eicar-Test-Signature FOUND");

        result.IsClean.Should().BeFalse();
        // The extracted signature (not the whole line, not "stream:") is the assertion.
        result.ThreatName.Should().Be("Eicar-Test-Signature");
    }

    [Fact]
    public void ClamAvParse_ErrorResponse_Throws_ISSUE101()
    {
        var act = () => ClamAvInStreamProtocol.ParseResponse("stream: INSTREAM size limit exceeded ERROR");

        // An ERROR reply is a SCAN FAILURE, never a (mis)detection — surfaced as the protocol
        // exception so the scanner applies its fail-open/fail-closed policy (same path as a socket fail).
        act.Should().Throw<ClamAvProtocolException>();
    }

    [Fact]
    public void ClamAvParse_GarbageResponse_Throws_ISSUE101()
    {
        var act = () => ClamAvInStreamProtocol.ParseResponse("gobbledygook not a clamd reply");

        act.Should().Throw<ClamAvProtocolException>();
    }

    [Fact]
    public void ClamAvParse_EmptyResponse_Throws_ISSUE101()
    {
        var act = () => ClamAvInStreamProtocol.ParseResponse("");

        act.Should().Throw<ClamAvProtocolException>();
    }

    // ── Response read: accumulates up to the NUL terminator ───────────────

    [Fact]
    public async Task ClamAvReadResponse_ReadsUpToNulTerminator_ISSUE101()
    {
        // clamd terminates its reply line with a single NUL byte; the reader accumulates the
        // whole line. The raw result carries the trailing NUL (the impl does not strip it),
        // so we trim it in the assertion and confirm the recognizable payload was read.
        using var source = new MemoryStream(Encoding.ASCII.GetBytes("stream: Eicar-Test-Signature FOUND\0"));

        var response = await ClamAvInStreamProtocol.ReadResponseAsync(source, CancellationToken.None);

        response.TrimEnd('\0').Should().Be("stream: Eicar-Test-Signature FOUND");
        // And the read result feeds ParseResponse to a real detection end-to-end.
        ClamAvInStreamProtocol.ParseResponse(response).ThreatName.Should().Be("Eicar-Test-Signature");
    }

    // ── DI gate: Host configured ⇒ real scanner; unset ⇒ allow-stub ───────

    [Fact]
    public void VirusScannerDi_HostUnset_UsesAllowStub_ISSUE101()
    {
        using var provider = BuildInfrastructureProvider(clamAvHost: null);

        // Resolving the allow-stub only logs — no socket. Prove the concrete wiring.
        provider.GetService<IVirusScanner>().Should().BeOfType<AllowWithLogVirusScanner>();
    }

    [Fact]
    public void VirusScannerDi_HostConfigured_UsesClamAv_ISSUE101()
    {
        using var provider = BuildInfrastructureProvider(clamAvHost: "clamav.internal");

        // The ClamAvVirusScanner ctor only stores IOptions + ILogger (the socket lives in
        // ScanAsync, which we never call), so resolving it opens NO connection.
        provider.GetService<IVirusScanner>().Should().BeOfType<ClamAvVirusScanner>();
    }

    private static ServiceProvider BuildInfrastructureProvider(string? clamAvHost)
    {
        // No connection string is supplied: AddInfrastructure defers UseNpgsql(null) until the
        // DbContext is first used, and this test only ever resolves the IVirusScanner singleton.
        var settings = new Dictionary<string, string?>();
        if (clamAvHost is not null)
            settings["VirusScanning:ClamAv:Host"] = clamAvHost;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(); // ClamAvVirusScanner / AllowWithLogVirusScanner both take an ILogger<>.
        services.AddInfrastructure(configuration);

        // No validate-on-build: we only ever resolve the IVirusScanner singleton, whose graph
        // (IOptions<ClamAvOptions> + ILogger) is fully satisfied above.
        return services.BuildServiceProvider();
    }
}
