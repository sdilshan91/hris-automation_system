// ============================================================================
// GAP-024 (second half) — a SWEEP job's interior log lines must name the tenant being swept.
//
// The first half (JobLogContextFilter) reads a tenant off a job's top-level `tenantId` ARGUMENT. That
// covers the per-tenant jobs Hangfire enqueues one-per-tenant, and it can never cover the ~19 sweep jobs:
// AutoClockOutJob.RunAsync(), LeaveAccrualJob.RunAsync(), ScheduledReportJob, WorkflowSlaEscalationJob,
// ProcessCarryForwardExpiryJob and siblings take NO parameters — they enumerate tenants internally and
// call ITenantJobRunner.RunForTenantAsync once per tenant. Nothing in the enqueued arguments tells a
// filter which tenant the loop is currently on.
//
// That left the higher-risk class unattributed: one sweep execution touches EVERY tenant's data, which is
// precisely the situation where isolation forensics needs to know which tenant a line came from. A sweep
// job is not context-free; it is SEQUENTIALLY single-tenant.
//
// So the push lives in TenantJobRunner, the seam every sweep routes through. These tests assert it against
// a real Serilog pipeline rather than reasoning about it, for the same reason the filter's half is:
// LogContext is an AsyncLocal, and a push that failed to flow into the work — or failed to pop after it —
// would compile, run, and look like a working control.
//
// The two failure modes that matter are both covered deliberately:
//   * NOT PUSHED  → lines carry no tenant (the original gap).
//   * NOT POPPED / one leaked ambient → EVERY iteration's lines carry ONE tenant, which is worse than no
//     attribution: it would positively misattribute tenant B's rows to tenant A. Hence two iterations with
//     DIFFERENT tenants asserted individually, plus a between-iterations arm.
// ============================================================================

using System.Reflection;
using FluentAssertions;
using HRM.Application.Common.Observability;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HRM.Tests.Unit;

[Trait("GAP", "GAP-024")]
[Trait("Category", "JobLogAttribution")]
public sealed class SweepJobTenantLogAttributionTests
{
    // ── a sweep iteration is attributed to ITS tenant ────────────────────────

    [Fact]
    public async Task Two_sweep_iterations_attribute_their_lines_to_their_own_tenants()
    {
        var runner = Runner();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (logger, events) = Pipeline();

        // The shape every sweep job has: no tenantId job argument, one runner call per tenant in a loop.
        foreach (var (tenantId, label) in new[] { (tenantA, "A"), (tenantB, "B") })
        {
            await runner.RunForTenantAsync(tenantId, $"tenant-{label}", _ =>
            {
                logger.Information("accrued leave for {Label}", label);
                return Task.CompletedTask;
            });
        }

        events.Should().HaveCount(2);
        TenantOf(events[0]).Should().Be(
            tenantA.ToString(),
            "the first iteration's interior lines belong to the tenant that iteration is sweeping");
        TenantOf(events[1]).Should().Be(
            tenantB.ToString(),
            "the second iteration must carry the SECOND tenant — asserting each value individually is what "
            + "stops a single leaked ambient tenant from satisfying this test");
        TenantOf(events[0]).Should().NotBe(
            TenantOf(events[1]),
            "if both iterations collapsed onto one value the sweep would MISATTRIBUTE one tenant's work to "
            + "another, which is worse for an isolation investigation than carrying no tenant at all");
    }

    // WHAT THE NEXT TWO ARMS DO AND DO NOT PROVE — measured, not assumed. Dropping the `using` in
    // TenantJobRunner leaves them GREEN. `AsyncTaskMethodBuilder.Start` restores the ExecutionContext around
    // an async method, so an AsyncLocal written inside RunForTenantAsync is structurally contained and never
    // reaches its caller whether or not it is popped. The `using` is therefore defence-in-depth, not the
    // mechanism, and claiming these arms guard it would be false.
    //
    // They are kept because they pin the CONTRACT an operator reads the log against — attribution stops at
    // the iteration boundary — and that contract does break under a plausible re-implementation: a push made
    // from a SYNCHRONOUS seam (e.g. inside TenantContext.SetTenant/SetSystemContext) escapes to its caller
    // and stains everything after it. A_system_context_job_emits_no_tenant_id is the arm that catches that
    // shape today; these two catch it for the per-tenant path if the push is ever hoisted out of the async
    // method.
    [Fact]
    public async Task The_attribution_is_released_when_the_iteration_ends()
    {
        var runner = Runner();
        var (logger, events) = Pipeline();

        await runner.RunForTenantAsync(Guid.NewGuid(), "acme", _ => Task.CompletedTask);
        logger.Information("moving on to the next tenant");

        events.Single().Properties.Should().NotContainKey(
            LogPropertyNames.TenantId,
            "the sweep's own between-tenants bookkeeping is not any one tenant's work; a property that "
            + "outlived its iteration would tag the REST of the sweep — and the next job on this worker — "
            + "with a tenant they have nothing to do with");
    }

    [Fact]
    public async Task An_exception_inside_an_iteration_still_releases_the_attribution()
    {
        // Sweeps are exactly where per-tenant failures happen (one tenant's bad data), and a failing
        // iteration is when the log is being read. `using` covers this; the arm pins it.
        var runner = Runner();
        var (logger, events) = Pipeline();

        var swept = async () => await runner.RunForTenantAsync(
            Guid.NewGuid(),
            "acme",
            _ => throw new InvalidOperationException("bad data for this tenant"));
        await swept.Should().ThrowAsync<InvalidOperationException>();

        logger.Information("continuing the sweep after a per-tenant failure");

        events.Single().Properties.Should().NotContainKey(LogPropertyNames.TenantId);
    }

    // ── a tenant-LESS job stays unattributed ─────────────────────────────────

    [Fact]
    public void A_system_context_job_emits_no_tenant_id()
    {
        var (logger, events) = Pipeline();
        var tenant = new TenantContext();

        // Exactly what TokenCleanupJob does at its top: declare cross-tenant scope, then work. It never
        // enters TenantJobRunner, so nothing pushes a tenant for it.
        tenant.SetSystemContext();
        logger.Information("purged expired refresh tokens");

        events.Single().Properties.Should().NotContainKey(
            LogPropertyNames.TenantId,
            "a deliberately cross-tenant job has no single tenant. Note TenantContext.SetSystemContext() "
            + "leaves TenantId as Guid.Empty, so an implementation that pushed from the tenant CONTEXT "
            + "instead of the per-tenant runner would stamp these lines 00000000-0000-0000-0000-000000000000 "
            + "— which reads like a real scope to whoever is grepping during an incident");
    }

    [Fact]
    public void TokenCleanupJob_really_is_tenant_less_rather_than_a_stand_in_this_test_invented()
    {
        // Keeps the arm above honest: it claims to model TokenCleanupJob, so that claim is checked. If the
        // job were ever converted to per-tenant work this goes red and the arm above must be re-examined,
        // instead of quietly modelling a job that no longer exists in that shape.
        var source = File.ReadAllText(JobSourcePath("TokenCleanupJob.cs"));

        source.Should().Contain("SetSystemContext(", "TokenCleanupJob declares cross-tenant scope");
        source.Should().NotContain(
            "ITenantJobRunner",
            "it must not route through the per-tenant seam — that seam is what attributes a tenant now");
        source.Should().NotContain("RunForTenantAsync");
    }

    [Fact]
    public async Task The_empty_guid_is_not_pushed_as_an_all_zero_scope()
    {
        // Preserves JobLogProperties' judgement at the second push site. Absent is the honest signal; an
        // all-zero tenant id is a plausible-looking lie.
        var runner = Runner();
        var (logger, events) = Pipeline();

        await runner.RunForTenantAsync(Guid.Empty, "unknown", _ =>
        {
            logger.Information("work under an unresolved tenant");
            return Task.CompletedTask;
        });

        events.Single().Properties.Should().NotContainKey(LogPropertyNames.TenantId);
    }

    // ── plumbing ────────────────────────────────────────────────────────────

    /// <summary>
    /// A runner on the InMemory provider with RLS off — the shipped dev/CI configuration, in which the
    /// runner sets the tenant context and runs the work directly. The log push sits ahead of that branch,
    /// so this exercises the same push either way; the GUC/transaction path is proved elsewhere
    /// (RlsIsolationPostgresTests).
    /// </summary>
    private static TenantJobRunner Runner()
    {
        var tenant = new TenantContext();
        AppDbContext db = TestDbContextFactory.Create(tenant);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Rls:Enabled"] = "false" })
            .Build();

        return new TenantJobRunner(db, tenant, configuration);
    }

    private static (ILogger Logger, List<LogEvent> Events) Pipeline()
    {
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            // The runner's property only surfaces through the LogContext enricher; without this the test
            // would be asserting against a pipeline that ignores the mechanism it exists to check.
            .Enrich.FromLogContext()
            .WriteTo.Sink(new CollectingSink(events))
            .CreateLogger();

        return (logger, events);
    }

    private static string? TenantOf(LogEvent logEvent)
        => logEvent.Properties.TryGetValue(LogPropertyNames.TenantId, out var value)
            ? value.ToString().Trim('"')
            : null;

    private static string JobSourcePath(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "src", "backend", "HRM.Api", "Jobs", fileName);
        File.Exists(path).Should().BeTrue($"the job source should be at {path}");
        return path;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "backend")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repo root (containing src/backend) should be an ancestor of the test assembly");
        return dir!.FullName;
    }

    private sealed class CollectingSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
