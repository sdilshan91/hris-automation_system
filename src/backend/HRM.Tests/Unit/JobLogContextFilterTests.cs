// ============================================================================
// GAP-024 — background-job log lines must carry job_name / job_id / tenant_id.
//
// Two halves are tested here for different reasons:
//
//   * JobLogProperties (pure) — WHICH properties get pushed. Straight logic, exhaustively covered.
//   * JobLogContextFilter (shell) — that the push actually REACHES the job. This is the part that could
//     silently do nothing: the filter pushes onto Serilog's LogContext in OnPerforming and then RETURNS,
//     and Hangfire invokes the job afterwards. That only works because LogContext is an AsyncLocal and
//     OnPerforming is synchronous (an AsyncLocal write in a sync callee is visible to its caller). If that
//     assumption were wrong the filter would compile, run, log nothing extra, and look like a working control.
//     So it is asserted against a real Serilog pipeline rather than reasoned about.
//
// SCOPE NOTE: this file covers only what a filter reading ENQUEUED ARGUMENTS can reach. The ~19 sweep jobs
// declare no tenantId parameter (they loop over tenants internally), so nothing here attributes them — and
// that is a limit of this mechanism, not a statement that their lines should go unattributed. Their half
// lives in SweepJobTenantLogAttributionTests, driven by TenantJobRunner.
// ============================================================================

using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.Storage;
using HRM.Api.Jobs.Filters;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HRM.Tests.Unit;

public sealed class JobLogContextFilterTests
{
    // ── the pure half: which properties ─────────────────────────────────────

    private static IReadOnlyDictionary<string, object> Properties(
        string? type = "LeaveAccrualJob",
        string? method = "RunAsync",
        string? jobId = "42",
        params (string Name, object? Value)[] arguments)
        => JobLogProperties
            .For(type, method, jobId, arguments.Select(a => new KeyValuePair<string, object?>(a.Name, a.Value)).ToList())
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    [Fact]
    public void A_per_tenant_job_is_attributed_to_its_tenant()
    {
        var tenantId = Guid.NewGuid();

        var properties = Properties(arguments: ("tenantId", tenantId));

        properties[JobLogProperties.JobNameKey].Should().Be("LeaveAccrualJob.RunAsync");
        properties[JobLogProperties.JobIdKey].Should().Be("42");
        properties[JobLogProperties.TenantIdKey].Should().Be(tenantId);
    }

    [Fact]
    public void A_tenant_id_serialized_as_a_string_is_still_recognised()
    {
        // Hangfire round-trips job arguments through JSON; a job typing its parameter as string is still
        // telling us its tenant.
        var tenantId = Guid.NewGuid();

        Properties(arguments: ("tenantId", tenantId.ToString()))[JobLogProperties.TenantIdKey]
            .Should().Be(tenantId);
    }

    [Fact]
    public void The_tenant_parameter_is_matched_case_insensitively()
    {
        var tenantId = Guid.NewGuid();

        Properties(arguments: ("TenantId", tenantId))[JobLogProperties.TenantIdKey].Should().Be(tenantId);
    }

    [Fact]
    public void A_cross_tenant_job_carries_no_tenant_id_rather_than_a_misleading_one()
    {
        // OnboardingOutboxReconcileJob declares SetSystemContext() and never enters ITenantJobRunner, so it
        // is genuinely tenant-less end to end and absence here is the final answer for it.
        Properties(type: "OnboardingOutboxReconcileJob", arguments: ("cancellationToken", null))
            .Should().NotContainKey(JobLogProperties.TenantIdKey);
    }

    [Fact]
    public void A_sweep_job_gets_no_tenant_from_the_FILTER_because_it_declares_no_tenant_argument()
    {
        // GAP-024 second half — PREMISE CORRECTION. The arm above used to be read as "no tenantId argument
        // ⇒ the job is cross-tenant ⇒ no attribution is correct", which conflated "cross-tenant" with
        // "iterates every tenant". The ~19 sweep jobs (LeaveAccrualJob.RunAsync() and siblings) take NO
        // parameters at all and enumerate tenants internally — they are SEQUENTIALLY single-tenant, and
        // they are the higher-risk class, since one execution touches every tenant's data.
        //
        // What is asserted here is a statement about the FILTER'S REACH, not about the job's nature: the
        // filter only ever sees the enqueued arguments, so it cannot know which tenant the loop is on and
        // must not guess. Attribution for these jobs is supplied per iteration by TenantJobRunner instead —
        // see SweepJobTenantLogAttributionTests. Absence HERE no longer means the job's lines go
        // unattributed; it means this mechanism is not the one that attributes them.
        Properties(type: "LeaveAccrualJob", method: "RunAsync")
            .Should().NotContainKey(JobLogProperties.TenantIdKey,
                "a filter reading enqueued arguments cannot see a loop variable — guessing a tenant here "
                + "would be a fabricated scope in the log");
    }

    [Fact]
    public void An_empty_guid_is_treated_as_absent_not_logged_as_a_scope()
    {
        // An all-zero tenant id reads like a real scope to whoever is grepping during an incident. Absent is
        // the honest signal.
        Properties(arguments: ("tenantId", Guid.Empty))
            .Should().NotContainKey(JobLogProperties.TenantIdKey);
    }

    [Fact]
    public void Another_guid_argument_is_not_mistaken_for_the_tenant()
    {
        // Guards the name match: "first Guid argument wins" would attribute a job to an EMPLOYEE id, which is
        // both wrong and plausible-looking in a log.
        Properties(arguments: ("employeeId", Guid.NewGuid()))
            .Should().NotContainKey(JobLogProperties.TenantIdKey);
    }

    [Fact]
    public void A_job_with_no_identifiable_name_pushes_no_job_name_property()
    {
        Properties(type: null, method: null, jobId: null)
            .Should().BeEmpty("a job_name of \".\" would be noise on every line");
    }

    // ── the shell: does the push reach the job? ─────────────────────────────

    [Fact]
    public void Properties_pushed_in_OnPerforming_are_visible_to_the_job_body()
    {
        var tenantId = Guid.NewGuid();
        var (filter, context, events, logger) = Arrange(tenantId);

        filter.OnPerforming(context);
        // Stands in for the job body, which Hangfire invokes after OnPerforming returns on the same context.
        logger.Information("work happened");

        var during = events.Single();
        during.Properties[JobLogProperties.TenantIdKey].ToString().Trim('"').Should().Be(tenantId.ToString(),
            "an AsyncLocal written by a SYNCHRONOUS callee stays visible to its caller — this is the whole "
            + "mechanism the filter relies on to reach a job body it never calls itself");
        during.Properties[JobLogProperties.JobNameKey].ToString().Trim('"')
            .Should().Be($"{nameof(JobLogContextFilterTests)}.{nameof(SampleTenantJob)}",
                "the name is read off the real Hangfire Job's reflected method, not from a string the filter "
                + "was handed");
    }

    [Fact]
    public void The_properties_do_not_leak_into_the_next_job_on_the_same_worker()
    {
        // The failure this prevents is worse than having no attribution at all: Hangfire reuses workers, so a
        // leaked tenant_id would attribute the NEXT job's lines to the PREVIOUS tenant.
        var (filter, context, events, logger) = Arrange(Guid.NewGuid());

        filter.OnPerforming(context);
        filter.OnPerformed(Performed(context, exception: null));
        logger.Information("the next job on this worker");

        events.Single().Properties.Should().NotContainKey(JobLogProperties.TenantIdKey);
    }

    [Fact]
    public void A_job_that_THREW_still_releases_its_log_scope()
    {
        // Hangfire calls OnPerformed for failed jobs too. If disposal were skipped on the failure path, one
        // throwing job would poison every subsequent job on that worker — and failures are exactly when the
        // log is being read.
        var (filter, context, events, logger) = Arrange(Guid.NewGuid());

        filter.OnPerforming(context);
        filter.OnPerformed(Performed(context, new InvalidOperationException("job blew up")));
        logger.Information("the next job on this worker");

        events.Single().Properties.Should().NotContainKey(JobLogProperties.TenantIdKey);
    }

    // ── plumbing ────────────────────────────────────────────────────────────

    /// <summary>A job signature with a tenantId parameter, mirroring the per-tenant jobs' shape.</summary>
    public static void SampleTenantJob(Guid tenantId)
    {
    }

    private static (JobLogContextFilter Filter, PerformingContext Context, List<LogEvent> Events, ILogger Logger)
        Arrange(Guid tenantId)
    {
        var job = new Job(
            typeof(JobLogContextFilterTests),
            typeof(JobLogContextFilterTests).GetMethod(nameof(SampleTenantJob))!,
            tenantId);

        var performContext = new PerformContext(
            storage: null,
            connection: Substitute.For<IStorageConnection>(),
            backgroundJob: new BackgroundJob("job-1", job, DateTime.UtcNow),
            cancellationToken: Substitute.For<IJobCancellationToken>());

        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            // The filter's properties only surface through the LogContext enricher; without this the test
            // would be asserting against a pipeline that ignores what it is testing.
            .Enrich.FromLogContext()
            .WriteTo.Sink(new CollectingSink(events))
            .CreateLogger();

        return (new JobLogContextFilter(), new PerformingContext(performContext), events, logger);
    }

    private static PerformedContext Performed(PerformingContext context, Exception? exception)
        => new(context, result: null, canceled: false, exception: exception);

    private sealed class CollectingSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
