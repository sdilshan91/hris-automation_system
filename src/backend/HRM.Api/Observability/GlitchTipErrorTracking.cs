using System.Reflection;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Sentry;
using Sentry.Extensibility;
using Serilog;
using Serilog.Configuration;

namespace HRM.Api.Observability;

/// <summary>
/// US-PLT-006: error tracking via self-hosted GlitchTip (Sentry-API-compatible). GlitchTip speaks the Sentry
/// protocol, so this wires the mature Sentry .NET SDK against a GlitchTip DSN. Two entry points, both
/// <b>additive</b> and both <b>safe-by-default</b>:
/// <list type="bullet">
///   <item><see cref="AddGlitchTipErrorTracking"/> — the ASP.NET Core integration (<c>UseSentry</c>) that captures
///   unhandled exceptions with stack + release, tags them per-tenant, and scrubs PII in <c>BeforeSend</c>
///   (AC-1/AC-2).</item>
///   <item><see cref="WriteToGlitchTip"/> — the Serilog <c>Error</c>-level sink added alongside the existing
///   console + file sinks (AC-4/AC-5).</item>
/// </list>
///
/// <para><b>Inert-by-default (AC-3):</b> both entry points no-op when <c>GlitchTip:Dsn</c> is blank/absent (the
/// shipped default), so with no DSN the SDK never initialises and makes <b>no network calls</b>. The real DSN is
/// supplied only via user-secrets / environment (never committed — Critical Rule #6, BR-3).</para>
///
/// <para>The existing OpenTelemetry wiring (<see cref="ObservabilityExtensions"/>) and the Serilog console/file
/// sinks are left untouched — GlitchTip composes with them, it does not replace them (AC-4/BR-4).</para>
/// </summary>
public static class GlitchTipErrorTracking
{
    /// <summary>Config key holding the GlitchTip project DSN. Blank in committed appsettings; real value via user-secrets/env.</summary>
    public const string DsnKey = "GlitchTip:Dsn";

    /// <summary>
    /// The inert-guard (AC-3): GlitchTip wiring is active only when a non-blank DSN is configured. This single
    /// predicate gates BOTH the ASP.NET Core integration and the Serilog sink, so a blank DSN keeps the whole
    /// integration inert (no SDK init, no network). Exposed for unit testing.
    /// </summary>
    public static bool IsEnabled(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration[DsnKey]);

    /// <summary>
    /// Wires the ASP.NET Core Sentry/GlitchTip integration when a DSN is configured; otherwise returns the builder
    /// unchanged (inert — AC-3). Captures unhandled exceptions with stack trace + release (AC-1), enforces
    /// <c>SendDefaultPii = false</c>, scrubs PII in <c>BeforeSend</c> (AC-2), and registers the per-tenant tag
    /// processor (AC-1). Only <c>Error</c>+ log events become issues (AC-5).
    /// </summary>
    public static IWebHostBuilder AddGlitchTipErrorTracking(this IWebHostBuilder webHost, IConfiguration configuration)
    {
        if (!IsEnabled(configuration))
            return webHost; // AC-3: no DSN ⇒ inert (no init, no network).

        var dsn = configuration[DsnKey];
        var release = ReleaseVersion();

        webHost.UseSentry(options =>
        {
            options.Dsn = dsn;
            options.SendDefaultPii = false;                 // NFR-1 / BR-2: never attach default PII.
            options.AttachStacktrace = true;                // AC-1: stack trace on captured events.
            options.Release = release;                      // AC-1 / FR-6: per-release regression tracking.
            options.MinimumEventLevel = LogLevel.Error;     // AC-5 / NFR-4: only Error+ log events → issues.
            // AC-2 / FR-4: strip request body, sensitive headers, cookies, query params, and PII fields before
            // the event leaves the process. Extracted, testable pure function (see SentryPiiScrubber).
            options.SetBeforeSend((SentryEvent @event) => SentryPiiScrubber.Scrub(@event));
        });

        // AC-1 / FR-5 / BR-5: scoped so it resolves the per-request ITenantContext to stamp tenant tags.
        webHost.ConfigureServices(services =>
            services.AddScoped<ISentryEventProcessor, TenantTagSentryEventProcessor>());

        return webHost;
    }

    /// <summary>
    /// Adds the GlitchTip Serilog sink ALONGSIDE the existing console + file sinks (AC-4/AC-5) when a DSN is
    /// configured; otherwise a no-op (inert — AC-3). Captures at <c>Error</c> level only and does NOT re-initialise
    /// the SDK (<c>InitializeSdk = false</c>) — it shares the hub that <see cref="AddGlitchTipErrorTracking"/>
    /// initialises, so there is no double init. The file sink remains the authoritative QA/<c>RequestId</c> log.
    /// </summary>
    public static LoggerConfiguration WriteToGlitchTip(this LoggerConfiguration loggerConfiguration, IConfiguration configuration)
    {
        if (!IsEnabled(configuration))
            return loggerConfiguration; // AC-3: no DSN ⇒ no sink added.

        return loggerConfiguration.WriteTo.Sentry(options =>
        {
            options.Dsn = configuration[DsnKey];
            options.InitializeSdk = false;                              // share the UseSentry-initialised hub.
            options.MinimumEventLevel = Serilog.Events.LogEventLevel.Error; // AC-5: Error level only.
            options.SendDefaultPii = false;                            // NFR-1.
        });
    }

    /// <summary>Application release/version stamped on captured events (AC-1/FR-6).</summary>
    private static string ReleaseVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
           ?? typeof(GlitchTipErrorTracking).Assembly.GetName().Version?.ToString()
           ?? "1.0.0";
}
