// ============================================================================
// GAP-024 — every background job must DECLARE its tenant scope.
//
// The gap register described this as three jobs missing SetSystemContext(). Measuring it found 15 job classes
// with no tenant declaration at all — 11 of which are client-side schedulers that only enqueue (no DB, and they
// run inside a request scope that already has context), plus 3 real job bodies. It also found the register wrong
// about DocumentExpiryNotificationJob, which does declare system context.
//
// That spread is the actual finding: with 62 job classes and no mechanical check, whether a job scopes itself is
// a matter of whether its author remembered. This is finding S-2's shape again (the RLS layer had a coverage
// guard and zero holes; the EF filter layer had none and six), so the fix is the same shape: a guard, not a
// one-time sweep of three files.
//
// Why this test reads SOURCE rather than using reflection: the declaration is a call inside a method body, which
// is invisible to reflection over types. Precedent for source-scanning as a coverage guard in this suite is
// FrontendPermissionLiteralTests (GAP-016).
//
// Why it matters NOW: GAP-001 inverts the unresolved-context default from PRIVILEGED to RESTRICTED. Every job
// that never declares a scope passes today by luck and fails afterwards. This guard is the prerequisite.
// ============================================================================

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace HRM.Tests.Unit;

public sealed class BackgroundJobTenantContextTests
{
    /// <summary>
    /// Classes under <c>HRM.Api/Jobs</c> that legitimately declare no tenant scope, each with the reason it
    /// needs none. Every one is a client-side ENQUEUER: it calls <c>IBackgroundJobClient</c> /
    /// <c>IRecurringJobManager</c> and touches no <c>DbContext</c>, and it runs inside the caller's request scope
    /// which already has tenant context. The second half of that claim is enforced by
    /// <see cref="TheSchedulerExemptions_TouchNoDbContext"/> — the exemption cannot be used to smuggle in a job
    /// that actually reads data.
    /// </summary>
    private static readonly HashSet<string> EnqueueOnlySchedulers =
    [
        "HangfireCyclePhaseScheduler",
        "HangfireExportJobScheduler",
        "HangfireHrReportExportJobScheduler",
        "HangfireInterviewReminderScheduler",
        "HangfireLeaveEntitlementRecalcScheduler",
        "HangfireOfferExpiryReminderScheduler",
        "HangfireOfferExpiryScheduler",
        "HangfirePayrollReportExportJobScheduler",
        "HangfirePayrollRunJobScheduler",
        "HangfirePayslipDistributionJobScheduler",
        "HangfirePayslipGenerationJobScheduler",
        "HangfireTenantDeletionScheduler",
    ];

    /// <summary>
    /// The three ways a job may declare its scope: cross-tenant on purpose, per-tenant via the runner, or the
    /// bare per-tenant set the runner wraps.
    /// </summary>
    private static readonly Regex DeclaresTenantScope = new(
        @"SetSystemContext\s*\(|RunForTenantAsync\s*\(|ITenantJobRunner|\.SetTenant\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void EveryBackgroundJob_DeclaresEitherSystemContextOrPerTenantScope()
    {
        var jobs = JobSourceFiles();
        jobs.Should().HaveCountGreaterThan(40,
            "the scan should find the whole Jobs folder — a broken path would make this test pass while " +
            "checking nothing");

        var undeclared = jobs
            .Where(job => !EnqueueOnlySchedulers.Contains(job.Name))
            .Where(job => !DeclaresTenantScope.IsMatch(job.Text))
            .Select(job => job.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        string.Join(", ", undeclared).Should().BeEmpty(
            "a background job runs with no HTTP request and therefore no TenantResolutionMiddleware, so it must " +
            "declare its own scope: SetSystemContext() when it is deliberately cross-tenant, or " +
            "ITenantJobRunner.RunForTenantAsync per tenant. Leaving the context unresolved is not a third " +
            "option — it is indistinguishable from having forgotten, it disables the EF global query filters " +
            "(which are written `!IsResolved || TenantId == ...`), and GAP-001 inverts the unresolved default " +
            "from privileged to restricted, so it will fail outright. If the job genuinely needs no data access, " +
            "add it to EnqueueOnlySchedulers with its reason.");
    }

    [Fact]
    public void TheSchedulerExemptions_TouchNoDbContext()
    {
        // Keeps the exemption list honest: it is for enqueuers, and an enqueuer that grew a query is no longer
        // exempt. Without this, the list would be a place to hide exactly the jobs this guard exists to catch.
        var offenders = JobSourceFiles()
            .Where(job => EnqueueOnlySchedulers.Contains(job.Name))
            .Where(job => job.Text.Contains("AppDbContext", StringComparison.Ordinal))
            .Select(job => job.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        string.Join(", ", offenders).Should().BeEmpty(
            "these are exempt only because they enqueue and never read data. One that resolves an AppDbContext " +
            "does read data, so it needs a declared tenant scope — remove it from EnqueueOnlySchedulers.");
    }

    [Fact]
    public void TheSchedulerExemptionList_HasNoStaleEntries()
    {
        // The other direction, same reason as FrontendPermissionLiteralTests' shrink-only check: a renamed or
        // deleted scheduler left behind here silently widens the exemption for a future file of that name.
        var actual = JobSourceFiles().Select(job => job.Name).ToHashSet(StringComparer.Ordinal);

        var stale = EnqueueOnlySchedulers
            .Where(name => !actual.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        string.Join(", ", stale).Should().BeEmpty(
            "these exemptions name files that no longer exist. Remove them, or a new job created under one of " +
            "these names would inherit an exemption nobody granted it.");
    }

    private static IReadOnlyList<(string Name, string Text)> JobSourceFiles()
    {
        var jobsDir = Path.Combine(RepoRoot(), "src", "backend", "HRM.Api", "Jobs");
        Directory.Exists(jobsDir).Should().BeTrue($"the job sources should be at {jobsDir}");

        return Directory
            .EnumerateFiles(jobsDir, "*.cs", SearchOption.AllDirectories)
            // Jobs/Filters holds the Hangfire server filter, not jobs.
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Filters{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Path.GetFileNameWithoutExtension(path), File.ReadAllText(path)))
            .ToList();
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
}
