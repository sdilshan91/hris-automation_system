// ============================================================================
// ISSUE-345: OpenTelemetry must be INERT by default (no exporter, no span
// recording, no stdout cost) and honour a kill-switch + a configured sampler.
//   - blank endpoint, no opt-in  ⇒ inert: NOTHING registered
//   - OTLP endpoint set           ⇒ enabled, OTLP exporter
//   - OpenTelemetry:Enabled=true  ⇒ enabled, Console exporter (explicit opt-in)
//   - OpenTelemetry:Enabled=false ⇒ hard kill-switch, inert even with endpoint
// ============================================================================

using FluentAssertions;
using HRM.Api.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using OpenTelemetry.Trace;

namespace HRM.Tests.Unit;

public sealed class ObservabilityExtensionsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    private static IServiceCollection Register(IConfiguration config)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Test");
        return new ServiceCollection().AddObservability(config, env);
    }

    private static bool OtelRegistered(IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(TracerProvider));

    // ── IsEnabled / ResolveExporterMode ──────────────────────────────────────

    [Fact]
    public void Default_BlankEndpoint_NoOptIn_IsInert()
    {
        var config = Config();
        ObservabilityExtensions.IsEnabled(config).Should().BeFalse();
        ObservabilityExtensions.ResolveExporterMode(config)
            .Should().Be(ObservabilityExtensions.ExporterMode.None);
    }

    [Fact]
    public void OtlpEndpointSet_IsEnabled_WithOtlpExporter()
    {
        var config = Config(("OpenTelemetry:OtlpEndpoint", "http://localhost:4317"));
        ObservabilityExtensions.IsEnabled(config).Should().BeTrue();
        ObservabilityExtensions.ResolveExporterMode(config)
            .Should().Be(ObservabilityExtensions.ExporterMode.Otlp);
    }

    [Fact]
    public void EnabledTrue_BlankEndpoint_UsesConsoleExporter()
    {
        var config = Config(("OpenTelemetry:Enabled", "true"));
        ObservabilityExtensions.IsEnabled(config).Should().BeTrue();
        ObservabilityExtensions.ResolveExporterMode(config)
            .Should().Be(ObservabilityExtensions.ExporterMode.Console);
    }

    [Fact]
    public void EnabledFalse_IsHardKillSwitch_EvenWithEndpoint()
    {
        var config = Config(
            ("OpenTelemetry:Enabled", "false"),
            ("OpenTelemetry:OtlpEndpoint", "http://localhost:4317"));
        ObservabilityExtensions.IsEnabled(config).Should().BeFalse();
        ObservabilityExtensions.ResolveExporterMode(config)
            .Should().Be(ObservabilityExtensions.ExporterMode.None);
    }

    // ── AddObservability registration (the observable cost) ───────────────────

    [Fact]
    public void AddObservability_Inert_RegistersNoOpenTelemetry()
    {
        // The whole point of ISSUE-345: blank endpoint + no opt-in ⇒ no OTel at all (no per-request span recording).
        OtelRegistered(Register(Config())).Should().BeFalse();
    }

    [Fact]
    public void AddObservability_KillSwitchOff_RegistersNothing()
    {
        var config = Config(
            ("OpenTelemetry:Enabled", "false"),
            ("OpenTelemetry:OtlpEndpoint", "http://localhost:4317"));
        OtelRegistered(Register(config)).Should().BeFalse();
    }

    [Fact]
    public void AddObservability_EndpointSet_RegistersOpenTelemetry()
    {
        var config = Config(("OpenTelemetry:OtlpEndpoint", "http://localhost:4317"));
        OtelRegistered(Register(config)).Should().BeTrue();
    }

    [Fact]
    public void AddObservability_ConsoleOptIn_RegistersOpenTelemetry()
    {
        var config = Config(("OpenTelemetry:Enabled", "true"));
        OtelRegistered(Register(config)).Should().BeTrue();
    }
    // ── ISSUE-345 sampler seam ────────────────────────────────────────────────
    // The implementing agent honestly reported that the inline SetSampler line survived mutation: the sampler
    // was wired, but nothing asserted the RATIO. That is the failure mode where you ship 100% sampling while
    // believing you configured 10% — the config looks right and nothing disagrees. These arms close it.

    [Fact]
    public void SamplingRatio_DefaultsToOne_WhenUnset_ISSUE345()
    {
        ObservabilityExtensions.ResolveSamplingRatio(Config()).Should().Be(1.0,
            "an unset ratio must keep today's sample-everything behaviour — this fix tunes sampling, it does "
            + "not silently start dropping traces on upgrade");
    }

    [Theory]
    [InlineData("0.1", 0.1)]
    [InlineData("0.5", 0.5)]
    [InlineData("0", 0.0)]
    [InlineData("1", 1.0)]
    public void SamplingRatio_HonoursConfiguredValue_ISSUE345(string configured, double expected)
    {
        // Discriminates against the pre-fix code, which ignored configuration entirely and always sampled 1.0:
        // every case except "1" fails under that implementation.
        ObservabilityExtensions.ResolveSamplingRatio(Config(("OpenTelemetry:SamplingRatio", configured)))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("2", 1.0)]      // >1 -> sample everything
    [InlineData("-1", 0.0)]     // <0 -> sample nothing
    [InlineData("not-a-number", 1.0)]  // uninferable -> default, not clamp
    public void SamplingRatio_ClampsRatherThanThrows_ISSUE345(string configured, double expected)
    {
        // A ratio is an operational knob, usually an env var set at deploy time. Fail-fasting the application
        // over a typo would turn a harmless config error into an outage, so out-of-range clamps predictably.
        var act = () => ObservabilityExtensions.ResolveSamplingRatio(
            Config(("OpenTelemetry:SamplingRatio", configured)));

        act.Should().NotThrow();
        act().Should().Be(expected);
    }

}
