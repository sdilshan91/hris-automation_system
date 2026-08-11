using System.Diagnostics;
using System.Security.Claims;
using HRM.Application.Common.Interfaces;
using Serilog.Core;
using Serilog.Events;

namespace HRM.Api.Observability;

/// <summary>
/// GAP-035 / §6.11-a — the two correlation fields every log record was missing: <c>user_id</c> and
/// <c>trace_id</c>. Measured against the running stack's log before building: of 1258 request-scoped property
/// bags, <c>RequestId</c> appeared in all of them and <c>user_id</c> / <c>trace_id</c> in <b>none</b>.
///
/// <para><b>Why enrichers and not a middleware.</b> A middleware can only enrich what flows through it: it would
/// miss every line written before it in the pipeline (tenant resolution, the exception handler), every EF Core SQL
/// line, and every background job. An <see cref="ILogEventEnricher"/> is evaluated per EVENT, so it covers 100% of
/// records with no pipeline placement at all — and <see cref="TraceContextEnricher"/> then also covers Hangfire
/// jobs for free, which is where <see cref="HRM.Api.Jobs.Filters.JobLogContextFilter"/> (GAP-024) left off.</para>
/// </summary>
public static class LogCorrelation
{
    /// <summary>W3C trace id — the id an OTLP collector will correlate on once one is stood up.</summary>
    public const string TraceIdKey = "trace_id";

    /// <summary>W3C span id of the active span.</summary>
    public const string SpanIdKey = "span_id";

    /// <summary>The EFFECTIVE user — while impersonating this is the TARGET, matching <c>ICurrentUser.UserId</c>.</summary>
    public const string UserIdKey = "user_id";

    /// <summary>The platform operator behind an impersonated session. Absent when not impersonating.</summary>
    public const string ImpersonatedByKey = "impersonated_by";
}

/// <summary>
/// Adds <c>trace_id</c> / <c>span_id</c> from the ambient <see cref="Activity"/>.
///
/// <para>Reads <see cref="Activity.Current"/> per event rather than capturing anything, so it is correct inside
/// requests, background jobs, and nested spans alike. Emits <b>nothing</b> when there is no activity — an
/// all-zero trace id (<c>00000000000000000000000000000000</c>) would look like a real id to whoever is grepping
/// during an incident, the same misleading-signal trap as logging <c>Guid.Empty</c> as a tenant.</para>
///
/// <para>Note this enricher is only half the fix: with OTel dormant (the shipped default) NOTHING registers an
/// <see cref="ActivityListener"/>, so ASP.NET Core never creates an activity and this would enrich nothing. See
/// <see cref="TraceContextActivation"/>.</para>
/// </summary>
public sealed class TraceContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            LogCorrelation.TraceIdKey, activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            LogCorrelation.SpanIdKey, activity.SpanId.ToString()));
    }
}

/// <summary>
/// Adds <c>user_id</c> (and <c>impersonated_by</c> during an impersonated session) from the current request's
/// principal.
///
/// <para>Reads the claims directly instead of resolving <c>ICurrentUser</c> from <c>RequestServices</c>: an
/// enricher runs for every log event including ones written as a request completes, and a DI resolve there can
/// hit a disposed scope. Claim PRECEDENCE is deliberately identical to <c>CurrentUser.UserId</c>
/// (<see cref="ClaimTypes.NameIdentifier"/> then <c>sub</c>) and that agreement is pinned by a test, so the two
/// cannot drift into disagreeing about who did something.</para>
///
/// <para><b>Impersonation is logged explicitly.</b> <c>ICurrentUser.UserId</c> is the impersonation TARGET, so a
/// bare <c>user_id</c> would attribute an operator's actions to the employee they were impersonating — a
/// misattribution in exactly the record an incident review reads. When the session is impersonated, the operator
/// is emitted alongside as <c>impersonated_by</c>.</para>
/// </summary>
public sealed class CurrentUserEnricher(IHttpContextAccessor httpContextAccessor) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            // Anonymous and non-HTTP (startup, background jobs) both land here: no user, so no property. An
            // empty or "anonymous" value would just be noise on every line.
            return;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(userId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(LogCorrelation.UserIdKey, userId));
        }

        var actorId = user.FindFirstValue(ImpersonationClaims.ActorId);
        if (!string.IsNullOrWhiteSpace(actorId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(LogCorrelation.ImpersonatedByKey, actorId));
        }
    }
}

/// <summary>
/// Makes <c>trace_id</c> actually exist when OpenTelemetry is dormant.
///
/// <para>ASP.NET Core only creates an <see cref="Activity"/> per request if something is listening. OTel is the
/// only thing here that ever listened, and it is endpoint-gated and inert by default — so on the shipped
/// configuration <see cref="Activity.Current"/> is null on every request and a trace enricher alone would be a
/// control that emits nothing. This registers a listener that samples
/// <see cref="ActivitySamplingResult.PropagationData"/>: the activity (and therefore the W3C trace id, including
/// one continued from an inbound <c>traceparent</c>) is created, but no tags or events are recorded — the
/// cheapest sampling level that still yields an id.</para>
///
/// <para><b>Registered only while OTel is disabled</b>, mirroring the house <c>IsEnabled</c> guard. When OTel is
/// on it brings its own listener and its own sampler, and forcing a second always-sample listener alongside it
/// would override sampling decisions and inflate export volume.</para>
/// </summary>
public static class TraceContextActivation
{
    /// <summary>
    /// Whether this app needs to supply its own listener. Separated from the registration below because
    /// <see cref="ActivitySource.AddActivityListener"/> is a PROCESS-GLOBAL side effect that cannot be undone —
    /// calling it from a test would leave every other test in the run creating activities. The decision is the
    /// part with a rule in it, so the decision is what gets tested.
    /// </summary>
    public static bool ShouldRegisterListener(IConfiguration configuration)
        => !ObservabilityExtensions.IsEnabled(configuration);

    public static void EnableTraceIdsWhenOtelIsDormant(IConfiguration configuration)
    {
        if (!ShouldRegisterListener(configuration))
        {
            return;
        }

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.PropagationData,
        });
    }
}
