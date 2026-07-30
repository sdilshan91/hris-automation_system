using System.Globalization;
using System.Reflection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

// NOTE: DB spans use Npgsql's built-in ActivitySource ("Npgsql", enabled via AddSource below) rather than the
// pre-release OpenTelemetry.Instrumentation.EntityFrameworkCore — the stable, plan-approved single DB span source.

namespace HRM.Api.Observability;

/// <summary>
/// P3 observability wiring (see <c>docs/Architecture/observability-otel-grafana-plan.md</c>): OpenTelemetry traces + metrics
/// for the HRM API. Grouped into a single extension (mirrors <c>AddInfrastructure</c>) so <c>Program.cs</c>
/// gains just one call.
///
/// <para><b>Inert-by-default (ISSUE-345).</b> Mirrors the <see cref="GlitchTipErrorTracking"/> blank-DSN-⇒-inert
/// house style: a single <see cref="IsEnabled"/> predicate gates ALL registration. When OTel is disabled the app
/// registers NO OpenTelemetry at all — no exporter, no per-request span recording, no stdout cost. This is
/// the shipped default, matching every doc that calls the instrumentation "DORMANT".</para>
/// <list type="bullet">
///   <item>OTLP endpoint <b>configured</b> (<c>OpenTelemetry:OtlpEndpoint</c>, or the standard
///   <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> env var) ⇒ enabled, export over OTLP/gRPC (default :4317) to the OTel
///   Collector, which fans out to Tempo/Prometheus/Loki (the prod-swappable seam in the plan doc).</item>
///   <item><c>OpenTelemetry:Enabled=true</c> with a <b>blank</b> endpoint ⇒ enabled with the <b>Console
///   exporter</b> — explicit local-visibility opt-in, never the default.</item>
///   <item>otherwise (blank endpoint, no explicit opt-in — the shipped default) ⇒ <b>inert</b>: nothing registered.</item>
///   <item><c>OpenTelemetry:Enabled=false</c> is a hard <b>kill-switch</b>: inert even when an endpoint is set.</item>
/// </list>
///
/// <para><b>Sampling (ISSUE-345 / plan §):</b> traces use a <c>ParentBasedSampler</c> over a
/// <c>TraceIdRatioBasedSampler</c> whose ratio is <c>OpenTelemetry:SamplingRatio</c> (default <c>1.0</c>),
/// so head sampling is tunable per environment instead of the previous unconditional 100%.</para>
///
/// <para>DB spans come from Npgsql's <b>stable</b> built-in <c>ActivitySource</c> (<c>AddSource("Npgsql")</c>);
/// <c>OpenTelemetry.Instrumentation.EntityFrameworkCore</c> is still pre-release, so per the plan doc's
/// "pick ONE DB span source" guidance we take the stable Npgsql one. Serilog is left untouched (file sink stays
/// the QA root-cause log). Per-tenant enrichment / custom meters (plan §1.3/§1.6) are deferred to a later slice.</para>
///
/// <para><b>Redis command-spans</b> (plan §5) are added via <c>AddRedisInstrumentation()</c>, gated on Redis being
/// configured. They cover the <b>shared</b> <c>IConnectionMultiplexer</c> — used by <c>IDistributedCache</c> and the
/// SignalR backplane (registered in <c>Program.cs</c>). The EF second-level cache uses its own provider-internal
/// multiplexer and is <b>NOT</b> covered (library limitation — see ISSUE-274).</para>
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>Exporter selected once OTel is enabled: OTLP when an endpoint is set, else Console.</summary>
    internal enum ExporterMode { None, Otlp, Console }

    /// <summary>Resolves the OTLP endpoint: <c>OpenTelemetry:OtlpEndpoint</c> first, then the standard
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> config key / env var (both spellings). Blank when none set.</summary>
    internal static string? ResolveOtlpEndpoint(IConfiguration configuration)
    {
        var endpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        }
        return string.IsNullOrWhiteSpace(endpoint) ? null : endpoint;
    }

    /// <summary>
    /// The inert-guard (ISSUE-345), mirroring <see cref="GlitchTipErrorTracking.IsEnabled"/>. OTel wiring is active
    /// only when: an OTLP endpoint is configured (a configured backend implies intent), OR <c>OpenTelemetry:Enabled</c>
    /// is explicitly <c>true</c>. An explicit <c>OpenTelemetry:Enabled=false</c> is a hard kill-switch that wins even
    /// when an endpoint is set. Blank endpoint + no explicit opt-in (the shipped default) ⇒ inert. Exposed for testing.
    /// </summary>
    public static bool IsEnabled(IConfiguration configuration)
    {
        // Explicit master switch wins in both directions (kill-switch and opt-in).
        if (configuration.GetValue<bool?>("OpenTelemetry:Enabled") is { } enabled)
            return enabled;
        // No explicit flag ⇒ enabled only when a backend endpoint is configured.
        return ResolveOtlpEndpoint(configuration) is not null;
    }

    /// <summary>
    /// Head-sampling ratio for <c>ParentBased(TraceIdRatioBased(ratio))</c> — <c>OpenTelemetry:SamplingRatio</c>,
    /// default <c>1.0</c> (sample all), now tunable per environment instead of the previous unconditional 100%.
    ///
    /// <para>Extracted as a public seam so the ratio is <b>testable</b>. The agent that implemented ISSUE-345
    /// honestly reported that the inline <c>SetSampler</c> line survived mutation — i.e. the sampler was wired
    /// but nothing asserted it. An unasserted sampler is how you end up shipping 100% sampling believing you
    /// configured 10%.</para>
    ///
    /// <para><b>Out-of-range values are clamped, not thrown.</b> A ratio is an operational knob, usually set by
    /// an env var at deploy time; fail-fasting the whole application because someone typed <c>2</c> or <c>-1</c>
    /// would turn a harmless config typo into an outage. Clamping to [0,1] degrades predictably: &gt;1 samples
    /// everything, &lt;0 samples nothing — both defensible readings of the intent. A non-numeric value falls back
    /// to the default rather than clamping, since no intent can be inferred from it.</para>
    /// </summary>
    public static double ResolveSamplingRatio(IConfiguration configuration)
    {
        // Read the RAW string and parse it ourselves. configuration.GetValue<double?>() THROWS
        // InvalidOperationException on an unparseable value — so `OpenTelemetry__SamplingRatio=abc`, an easy
        // env-var typo at deploy time, would take the whole application down at startup over a telemetry knob.
        // Discovered by the arm below, which was written expecting GetValue to return null and instead failed.
        var raw = configuration["OpenTelemetry:SamplingRatio"];
        if (string.IsNullOrWhiteSpace(raw))
            return 1.0;

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)
            || double.IsNaN(ratio))
        {
            return 1.0; // uninferable ⇒ default; no intent can be read from garbage, so do not clamp it
        }

        return Math.Clamp(ratio, 0.0, 1.0);
    }

    /// <summary>Which exporter (if any) AddObservability will register for the given config. Exposed for testing.</summary>
    internal static ExporterMode ResolveExporterMode(IConfiguration configuration)
    {
        if (!IsEnabled(configuration))
            return ExporterMode.None;
        return ResolveOtlpEndpoint(configuration) is not null ? ExporterMode.Otlp : ExporterMode.Console;
    }

    /// <summary>
    /// US-PLT-004 (item 1): whether the Serilog OTLP <b>log</b> sink should be registered. Gated on the same
    /// <see cref="IsEnabled"/> guard AND an actually-resolved OTLP endpoint — an OTLP log sink with no endpoint
    /// would blindly ship logs at <c>localhost:4317</c> with no collector, producing connection noise. So the
    /// log sink follows the exact OTLP-only rule the trace/metric exporters use (Console-mode ⇒ no OTLP log
    /// sink; the existing Serilog Console+File sinks already cover local visibility). Exposed for testing.
    /// </summary>
    public static bool IsLogExportEnabled(IConfiguration configuration)
        => IsEnabled(configuration) && ResolveOtlpEndpoint(configuration) is not null;

    /// <summary>
    /// US-PLT-004 (item 1): adds the OTLP log sink ALONGSIDE the existing Serilog Console + File sinks, mirroring
    /// <c>WriteToGlitchTip</c>. Inert when OTel is inert (or Console-mode) — see <see cref="IsLogExportEnabled"/> —
    /// so the file sink (the authoritative QA/<c>RequestId</c> root-cause log) is never touched or replaced. When
    /// enabled it exports over OTLP/gRPC to the SAME endpoint the trace/metric exporters use, tagged with the same
    /// <c>service.name</c> resource attribute for correlation.
    /// </summary>
    public static LoggerConfiguration WriteToOpenTelemetry(
        this LoggerConfiguration loggerConfiguration, IConfiguration configuration)
    {
        if (!IsLogExportEnabled(configuration))
            return loggerConfiguration; // inert / Console-mode ⇒ no OTLP log sink added.

        var endpoint = ResolveOtlpEndpoint(configuration)!;
        return loggerConfiguration.WriteTo.OpenTelemetry(options =>
        {
            options.Endpoint = endpoint;
            options.Protocol = OtlpProtocol.Grpc; // match the trace/metric OTLP exporters (gRPC :4317 default).
            options.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = "HRM.Api",
            };
        });
    }

    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Inert-by-default (ISSUE-345): register NOTHING unless explicitly enabled. This is what every doc means
        // by "DORMANT" — no exporter, no per-request span recording, no stdout write.
        var exporterMode = ResolveExporterMode(configuration);
        if (exporterMode == ExporterMode.None)
            return services;

        var otlpEndpoint = ResolveOtlpEndpoint(configuration);
        var useOtlp = exporterMode == ExporterMode.Otlp;

        var samplingRatio = ResolveSamplingRatio(configuration);

        // Redis command-spans (plan §5) are gated on Redis being configured: AddRedisInstrumentation() resolves the
        // shared IConnectionMultiplexer from DI (registered by Program.cs AddSharedRedisMultiplexer), which only
        // exists when a Redis connection string is set. In dev/in-memory mode (no Redis) it must NOT be added, or it
        // would try to resolve a missing multiplexer.
        var redisConfigured = !string.IsNullOrWhiteSpace(
            configuration.GetConnectionString("Redis") ?? configuration["Redis:ConnectionString"]);

        var serviceVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "HRM.Api", serviceVersion: serviceVersion)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment", environment.EnvironmentName),
                }))
            .WithTracing(tracing =>
            {
                // ISSUE-345: parent-based head sampling (respect an upstream sampling decision, else ratio-sample
                // the root). Ratio 1.0 = sample all; lower it per environment to cut trace volume/cost.
                tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)));

                tracing
                    .AddAspNetCoreInstrumentation(o => o.RecordException = true)
                    .AddHttpClientInstrumentation()
                    .AddSource("Npgsql")  // stable, built-in Npgsql DB spans
                    .AddSource("HRM.*");  // any future manual ActivitySources

                // Redis command-spans for the SHARED multiplexer (IDistributedCache + SignalR backplane). Resolves
                // the DI-registered IConnectionMultiplexer; only added when Redis is configured (see gate above).
                if (redisConfigured)
                {
                    tracing.AddRedisInstrumentation();
                }

                if (useOtlp)
                {
                    tracing.AddOtlpExporter(e => e.Endpoint = new Uri(otlpEndpoint!));
                }
                else
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("HRM.*");   // any future custom meters

                if (useOtlp)
                {
                    metrics.AddOtlpExporter(e => e.Endpoint = new Uri(otlpEndpoint!));
                }
                else
                {
                    metrics.AddConsoleExporter();
                }
            });

        return services;
    }
}
