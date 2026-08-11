// ============================================================================
// GAP-035 — user_id and trace_id on every log record.
//
// The gap was measured against the RUNNING stack's log before any code was written: of 1258 request-scoped
// property bags, RequestId appeared in all of them and user_id / trace_id in none. Both output templates already
// render {Properties:j}, so the only thing missing was the properties themselves.
//
// These run the enrichers through a REAL Serilog pipeline rather than calling Enrich() and inspecting the event,
// because "the property is produced" and "the property reaches the sink" are different claims and only the
// second one is the deliverable.
// ============================================================================

using System.Diagnostics;
using System.Security.Claims;
using FluentAssertions;
using HRM.Api.Observability;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HRM.Tests.Unit;

public sealed class LogCorrelationEnricherTests
{
    // ── user_id ─────────────────────────────────────────────────────────────

    [Fact]
    public void An_authenticated_request_stamps_user_id_on_the_log_record()
    {
        var userId = Guid.NewGuid();
        var (logger, events) = PipelineFor(new CurrentUserEnricher(AccessorFor(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()))));

        logger.Information("something happened");

        Property(events, LogCorrelation.UserIdKey).Should().Be(userId.ToString());
    }

    [Fact]
    public void The_sub_claim_is_honoured_when_NameIdentifier_is_absent()
    {
        var userId = Guid.NewGuid();
        var (logger, events) = PipelineFor(new CurrentUserEnricher(AccessorFor(new Claim("sub", userId.ToString()))));

        logger.Information("something happened");

        Property(events, LogCorrelation.UserIdKey).Should().Be(userId.ToString());
    }

    [Fact]
    public void The_enricher_and_CurrentUser_agree_on_WHO_the_user_is()
    {
        // The enricher reads claims directly rather than resolving ICurrentUser (a DI resolve per log event can
        // hit a disposed scope as a request completes). That duplication is only safe while the two agree, so
        // the agreement is pinned here instead of left to a comment. Both claims present, in conflict, to prove
        // the PRECEDENCE matches and not merely the happy path.
        var nameIdentifier = Guid.NewGuid();
        var sub = Guid.NewGuid();
        var accessor = AccessorFor(
            new Claim(ClaimTypes.NameIdentifier, nameIdentifier.ToString()),
            new Claim("sub", sub.ToString()));

        var (logger, events) = PipelineFor(new CurrentUserEnricher(accessor));
        logger.Information("something happened");

        var fromCurrentUser = new CurrentUser(accessor).UserId;
        Property(events, LogCorrelation.UserIdKey).Should().Be(fromCurrentUser.ToString());
        fromCurrentUser.Should().Be(nameIdentifier, "and NameIdentifier is the one that should win");
    }

    [Fact]
    public void An_impersonated_session_records_the_OPERATOR_as_well_as_the_target()
    {
        // ICurrentUser.UserId is the impersonation TARGET, so user_id alone would attribute an operator's
        // actions to the employee they were impersonating — in exactly the record an incident review reads.
        var target = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var (logger, events) = PipelineFor(new CurrentUserEnricher(AccessorFor(
            new Claim(ClaimTypes.NameIdentifier, target.ToString()),
            new Claim(ImpersonationClaims.IsImpersonation, "true"),
            new Claim(ImpersonationClaims.ActorId, operatorId.ToString()))));

        logger.Information("something happened");

        Property(events, LogCorrelation.UserIdKey).Should().Be(target.ToString());
        Property(events, LogCorrelation.ImpersonatedByKey).Should().Be(operatorId.ToString());
    }

    [Fact]
    public void An_ordinary_session_carries_no_impersonated_by()
    {
        var (logger, events) = PipelineFor(new CurrentUserEnricher(AccessorFor(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()))));

        logger.Information("something happened");

        events.Single().Properties.Should().NotContainKey(LogCorrelation.ImpersonatedByKey);
    }

    [Fact]
    public void Anonymous_traffic_carries_no_user_id_rather_than_an_empty_one()
    {
        var (logger, events) = PipelineFor(new CurrentUserEnricher(AccessorFor(authenticated: false)));

        logger.Information("something happened");

        events.Single().Properties.Should().NotContainKey(LogCorrelation.UserIdKey);
    }

    [Fact]
    public void A_log_written_outside_any_request_does_not_throw_and_adds_no_user()
    {
        // Startup and background jobs both log with a null HttpContext. An enricher that throws there would
        // take out logging exactly when it is most needed.
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var (logger, events) = PipelineFor(new CurrentUserEnricher(accessor));

        logger.Information("startup");

        events.Single().Properties.Should().NotContainKey(LogCorrelation.UserIdKey);
    }

    // ── trace_id ────────────────────────────────────────────────────────────

    [Fact]
    public void A_log_written_inside_an_activity_carries_its_trace_and_span_ids()
    {
        using var listener = ListenToEverything();
        using var source = new ActivitySource("HRM.Tests.LogCorrelation");
        var (logger, events) = PipelineFor(new TraceContextEnricher());

        using var activity = source.StartActivity("request");
        activity.Should().NotBeNull("the listener above is what makes an activity exist at all");
        logger.Information("inside the request");

        Property(events, LogCorrelation.TraceIdKey).Should().Be(activity!.TraceId.ToString());
        Property(events, LogCorrelation.SpanIdKey).Should().Be(activity.SpanId.ToString());
    }

    [Fact]
    public void With_no_activity_no_trace_id_is_emitted_rather_than_an_all_zero_one()
    {
        // 00000000000000000000000000000000 looks like a real id to whoever is grepping during an incident —
        // the same misleading-signal trap as logging Guid.Empty as a tenant (GAP-024).
        Activity.Current.Should().BeNull("this test asserts the no-activity path");
        var (logger, events) = PipelineFor(new TraceContextEnricher());

        logger.Information("no activity here");

        var properties = events.Single().Properties;
        properties.Should().NotContainKey(LogCorrelation.TraceIdKey);
        properties.Should().NotContainKey(LogCorrelation.SpanIdKey);
    }

    [Fact]
    public void An_inbound_traceparent_is_continued_rather_than_restarted()
    {
        // The point of a W3C trace id over a locally-invented one: a request arriving with a traceparent stays
        // on the SAME trace, so a log line here can be joined to the caller's.
        using var listener = ListenToEverything();
        using var source = new ActivitySource("HRM.Tests.LogCorrelation");
        var (logger, events) = PipelineFor(new TraceContextEnricher());

        var inbound = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        using var activity = source.StartActivity("request", ActivityKind.Server, inbound);
        logger.Information("inside the request");

        Property(events, LogCorrelation.TraceIdKey).Should().Be(inbound.TraceId.ToString());
    }

    [Fact]
    public void PropagationData_sampling_still_produces_a_usable_trace_id()
    {
        // TraceContextActivation deliberately samples at PropagationData (the cheapest level) when OTel is
        // dormant. That is only worth doing if an id still comes out of it.
        using var listener = ListenToEverything(ActivitySamplingResult.PropagationData);
        using var source = new ActivitySource("HRM.Tests.LogCorrelation");
        var (logger, events) = PipelineFor(new TraceContextEnricher());

        using var activity = source.StartActivity("request");
        activity.Should().NotBeNull();
        logger.Information("inside the request");

        Property(events, LogCorrelation.TraceIdKey).Should().Be(activity!.TraceId.ToString())
            .And.NotBe("00000000000000000000000000000000");
    }

    // ── when the app must supply its own listener ───────────────────────────

    [Fact]
    public void With_OTel_dormant_the_app_registers_its_own_listener()
    {
        // The shipped default. Without this, nothing listens, ASP.NET Core never creates an activity, and the
        // trace enricher would emit nothing on every request — a control that looks present and is not.
        TraceContextActivation.ShouldRegisterListener(ConfigurationWith()).Should().BeTrue();
    }

    [Fact]
    public void With_OTel_enabled_the_app_does_NOT_add_a_second_listener()
    {
        // OTel brings its own listener AND its own sampler. A second always-sample listener alongside it would
        // override sampling decisions and inflate export volume, so this must stay off when OTel is on.
        TraceContextActivation
            .ShouldRegisterListener(ConfigurationWith(("OpenTelemetry:OtlpEndpoint", "http://localhost:4317")))
            .Should().BeFalse();
    }

    private static IConfiguration ConfigurationWith(params (string Key, string Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    // ── plumbing ────────────────────────────────────────────────────────────

    private static IHttpContextAccessor AccessorFor(params Claim[] claims) => AccessorFor(true, claims);

    private static IHttpContextAccessor AccessorFor(bool authenticated, params Claim[] claims)
    {
        // "Test" as the authentication type is what makes ClaimsIdentity.IsAuthenticated true; a null scheme
        // yields an unauthenticated identity, which is how the anonymous case is built.
        var identity = authenticated ? new ClaimsIdentity(claims, "Test") : new ClaimsIdentity();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
        return accessor;
    }

    private static (ILogger Logger, List<LogEvent> Events) PipelineFor(ILogEventEnricher enricher)
    {
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(enricher)
            .WriteTo.Sink(new CollectingSink(events))
            .CreateLogger();

        return (logger, events);
    }

    private static string Property(List<LogEvent> events, string key)
    {
        events.Should().ContainSingle();
        events[0].Properties.Should().ContainKey(key);
        return events[0].Properties[key].ToString().Trim('"');
    }

    /// <summary>The same trick <see cref="TraceContextActivation"/> uses: without a listener, no activity exists.</summary>
    private static ActivityListener ListenToEverything(
        ActivitySamplingResult sampling = ActivitySamplingResult.AllData)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "HRM.Tests.LogCorrelation",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => sampling,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => sampling,
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class CollectingSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
