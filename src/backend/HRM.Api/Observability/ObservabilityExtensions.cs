using System.Reflection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// NOTE: DB spans use Npgsql's built-in ActivitySource ("Npgsql", enabled via AddSource below) rather than the
// pre-release OpenTelemetry.Instrumentation.EntityFrameworkCore — the stable, plan-approved single DB span source.

namespace HRM.Api.Observability;

/// <summary>
/// P3 observability wiring (see <c>docs/observability-otel-grafana-plan.md</c>): OpenTelemetry traces + metrics
/// for the HRM API. Grouped into a single extension (mirrors <c>AddInfrastructure</c>) so <c>Program.cs</c>
/// gains just one call.
///
/// <para><b>Exporter selection is ENDPOINT-GATED and safe-by-default.</b> The app reads an OTLP endpoint from
/// <c>OpenTelemetry:OtlpEndpoint</c> (falling back to the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> env var):
/// </para>
/// <list type="bullet">
///   <item>endpoint <b>configured</b> ⇒ export over OTLP/gRPC (default :4317) to the OTel Collector, which fans
///   out to Tempo/Prometheus/Loki (the prod-swappable seam in the plan doc).</item>
///   <item>endpoint <b>blank</b> (the shipped default) ⇒ <b>Console exporter only</b>. No external dependency, so
///   the app starts and runs normally with NO collector — no hang, no crash — and spans are still dev-visible.</item>
/// </list>
///
/// <para>DB spans come from Npgsql's <b>stable</b> built-in <c>ActivitySource</c> (<c>AddSource("Npgsql")</c>);
/// <c>OpenTelemetry.Instrumentation.EntityFrameworkCore</c> is still pre-release, so per the plan doc's
/// "pick ONE DB span source" guidance we take the stable Npgsql one. Serilog is left untouched (file sink stays
/// the QA root-cause log). Per-tenant enrichment / custom meters (plan §1.3/§1.6) are deferred to a later slice.</para>
/// </summary>
public static class ObservabilityExtensions
{
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Endpoint-gated exporter: config key first, then the standard OTEL_ env var (both spellings).
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        }
        var useOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

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
                tracing
                    .AddAspNetCoreInstrumentation(o => o.RecordException = true)
                    .AddHttpClientInstrumentation()
                    .AddSource("Npgsql")  // stable, built-in Npgsql DB spans
                    .AddSource("HRM.*");  // any future manual ActivitySources

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
