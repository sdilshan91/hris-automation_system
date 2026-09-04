using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Guards the DEV-only demo data <see cref="E2EDemoDataSeeder"/> puts into the <c>e2e</c> tenant.
///
/// <para><b>What this is for.</b> The E2E tenant used to hold ONE employee, ZERO appraisal cycles and ZERO
/// offboarding instances, so the Performance and Offboarding screens could not be exercised at all on the
/// dev/QA stack — a reviewer could not tell "works, no data" from "broken". These arms assert the data is
/// really in Postgres, is stamped with the right tenant, and survives a second seeder run unchanged.</para>
///
/// <para><b>Why this runs against the real host.</b> It reads back what production
/// <c>DbInitializer.RunAsync</c> actually wrote, rather than calling the seeder itself and admiring the
/// result. <c>ApiTestFactory</c> boots in <c>Development</c>, which is the branch the E2E seed lives behind —
/// so this suite also transitively proves the seeder is WIRED, not merely present.</para>
///
/// <para><b>Counts are exact, not "greater than zero".</b> A &gt;0 assertion would still pass on the cycle
/// with zero participants that renders identically to no cycle at all — precisely the bug being fixed.
/// Employee counts are scoped to the <c>E2E-</c> prefix so an unrelated suite in the shared
/// <c>HttpApi</c> collection creating its own employee cannot make this flap.</para>
/// </summary>
[Collection("HttpApi")]
[Trait("TC", "TC-QA-SEED-001")]
public sealed class E2EDemoSeedApiTests
{
    // Deliberately the literal, not the constant: a rename of the DEV tenant should break HERE rather than
    // be silently followed, because the compose stack and the Playwright layer hard-code it too.
    private const string E2ESubdomain = "e2e";

    private readonly ApiTestFactory _factory;

    public E2EDemoSeedApiTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task DevSeed_BuildsAnOrgChartWithAManagerAndSeveralDirectReports()
    {
        var (db, tenantId) = await ArrangeAsync();

        var employees = await db.Employees
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.EmployeeNo.StartsWith("E2E-"))
            .ToListAsync();

        employees.Should().HaveCount(10, "the demo org chart is the owner plus nine seeded employees");
        employees.Should().OnlyContain(e => e.TenantId == tenantId);

        var owner = employees.Single(e => e.EmployeeNo == E2EDemoDataSeeder.OwnerEmployeeNo);
        owner.ReportsToEmployeeId.Should().BeNull("the owner is the root of the tree");

        // At least one manager with SEVERAL direct reports — otherwise a manager's "my team" view and the
        // org tree both render a single node, which is the empty state this seed exists to remove.
        var manager = employees.Single(e => e.EmployeeNo == "E2E-0002");
        employees.Count(e => e.ReportsToEmployeeId == manager.Id)
            .Should().Be(5, "E2E-0002 anchors the engineering subtree");
        employees.Count(e => e.ReportsToEmployeeId == owner.Id).Should().Be(4);

        // Every employee is reachable from the root: no orphan with a dangling manager pointer.
        var ids = employees.Select(e => e.Id).ToHashSet();
        employees.Where(e => e.ReportsToEmployeeId is not null)
            .Should().OnlyContain(e => ids.Contains(e.ReportsToEmployeeId!.Value));

        employees.Should().Contain(e => e.Status == EmployeeStatus.Terminated,
            "an offboarding-eligible/exited employee must exist for the offboarding screens");
    }

    [Fact]
    public async Task DevSeed_CreatesAnInProgressAppraisalCycleWithPhasesAndParticipants()
    {
        var (db, tenantId) = await ArrangeAsync();

        var cycle = await db.AppraisalCycles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Name == E2EDemoDataSeeder.DemoCycleName);

        cycle.Should().NotBeNull("the DEV seed must create the demo appraisal cycle");
        cycle!.TenantId.Should().Be(tenantId);
        cycle.Status.Should().Be(AppraisalCycleStatus.Active);
        cycle.Is360Enabled.Should().BeTrue("the 360 screens are among those that could not be verified");

        var phases = await db.CyclePhases
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.CycleId == cycle.Id)
            .ToListAsync();

        phases.Should().HaveCount(5);
        phases.Should().OnlyContain(p => p.TenantId == tenantId);

        // "In progress" is the load-bearing property: a cycle whose windows have all elapsed is as
        // unexercisable as no cycle. Assert the cycle is open NOW and that a phase is currently running.
        var now = DateTime.UtcNow;
        cycle.StartDate.Should().BeBefore(now);
        cycle.EndDate.Should().BeAfter(now);
        phases.Should().Contain(p => p.StartDate <= now && p.EndDate >= now,
            "some phase must be open at the moment the dev stack is used");

        var participants = await db.CycleParticipants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.CycleId == cycle.Id)
            .ToListAsync();

        participants.Should().HaveCount(9,
            "a cycle with zero participants renders identically to no cycle at all");
        participants.Should().OnlyContain(p => p.TenantId == tenantId);
    }

    [Fact]
    public async Task DevSeed_Creates360FeedbackWithBothSubmittedAndPendingReviewers()
    {
        var (db, tenantId) = await ArrangeAsync();
        var cycleId = await DemoCycleIdAsync(db, tenantId);

        var assignments = await db.ReviewerAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.CycleId == cycleId)
            .ToListAsync();

        assignments.Should().HaveCount(10);
        assignments.Should().OnlyContain(a => a.TenantId == tenantId);
        assignments.Count(a => a.Status == ReviewerAssignmentStatus.Completed).Should().Be(6);
        assignments.Count(a => a.Status == ReviewerAssignmentStatus.Pending).Should().Be(4,
            "the 'feedback requested of me' screens need an outstanding request too");

        var feedback = await db.Feedback360s
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.CycleId == cycleId)
            .ToListAsync();

        feedback.Should().HaveCount(6);
        feedback.Should().OnlyContain(f => f.TenantId == tenantId);

        // All four reviewer categories are represented, so the composite-score weighting has every input.
        feedback.Select(f => f.Category).Distinct().Should().BeEquivalentTo(new[]
        {
            ReviewerCategory.Self,
            ReviewerCategory.Manager,
            ReviewerCategory.Peer,
            ReviewerCategory.DirectReport,
        });

        var feedbackIds = feedback.Select(f => f.Id).ToList();
        var items = await db.Feedback360Items
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && feedbackIds.Contains(i.Feedback360Id))
            .ToListAsync();

        items.Should().HaveCount(18, "three rated competencies per submitted response");
        items.Should().OnlyContain(i => i.TenantId == tenantId);
        items.Should().OnlyContain(i => i.Rating >= 1 && i.Rating <= 5);
    }

    [Fact]
    public async Task DevSeed_CreatesOffboardingInstancesWithTaskInstances()
    {
        var (db, tenantId) = await ArrangeAsync();

        var instances = await db.OffboardingInstances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.TemplateName == E2EDemoDataSeeder.DemoOffboardingTemplateName)
            .ToListAsync();

        instances.Should().HaveCount(2);
        instances.Should().OnlyContain(o => o.TenantId == tenantId);
        instances.Should().Contain(o => o.Status == OffboardingStatus.InProgress,
            "an open clearance checklist is the state the offboarding screens are operated in");
        instances.Should().Contain(o => o.Status == OffboardingStatus.Completed);

        // Each instance is attached to a real employee of the SAME tenant — a task list hanging off a
        // dangling employee id would render blank rows and look like a UI bug.
        var employeeIds = await db.Employees
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Select(e => e.Id)
            .ToListAsync();
        instances.Should().OnlyContain(o => employeeIds.Contains(o.EmployeeId));

        var instanceIds = instances.Select(o => o.Id).ToList();
        var tasks = await db.OffboardingTaskInstances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && instanceIds.Contains(t.OffboardingInstanceId))
            .ToListAsync();

        tasks.Should().HaveCount(10);
        tasks.Should().OnlyContain(t => t.TenantId == tenantId);
        tasks.Should().Contain(t => t.Status == OnboardingTaskStatus.Pending);
        tasks.Should().Contain(t => t.Status == OnboardingTaskStatus.Completed);
    }

    /// <summary>
    /// The seeder runs on EVERY app start. This re-runs it against the already-seeded database — exactly what
    /// restart #2 does — and asserts not one row is duplicated. Run it twice, count before and after.
    /// </summary>
    [Fact]
    public async Task DevSeed_IsIdempotent_ASecondRunAddsNoRows()
    {
        var (db, tenantId) = await ArrangeAsync();

        var ownerEmployeeId = await db.Employees
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.EmployeeNo == E2EDemoDataSeeder.OwnerEmployeeNo)
            .Select(e => e.Id)
            .SingleAsync();

        var before = await CountsAsync(db, tenantId);

        // Sanity: idempotency over an EMPTY set is trivially true and would let a deleted seed block pass
        // this arm. Require the first run to have actually produced something.
        before.Values.Should().OnlyContain(v => v > 0,
            "the first (production) seeder run must have inserted rows for this arm to mean anything");

        await E2EDemoDataSeeder.SeedAsync(db, tenantId, ownerEmployeeId, NullLogger.Instance, default);

        var after = await CountsAsync(db, tenantId);
        after.Should().BeEquivalentTo(before, "re-running the seeder must not duplicate a single row");
    }

    /// <summary>
    /// Tenant isolation (Critical Rule #1): none of the demo rows may leak into another tenant. The platform
    /// tenant is seeded by the same <c>RunAsync</c> pass, so it is the right negative control.
    /// </summary>
    [Fact]
    public async Task DevSeed_DoesNotLeakIntoAnotherTenant()
    {
        var (db, tenantId) = await ArrangeAsync();

        var otherTenantIds = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Id != tenantId)
            .Select(t => t.Id)
            .ToListAsync();

        otherTenantIds.Should().NotBeEmpty("the platform tenant is the negative control");

        (await db.AppraisalCycles.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(c => c.Name == E2EDemoDataSeeder.DemoCycleName && otherTenantIds.Contains(c.TenantId)))
            .Should().Be(0);

        (await db.OffboardingInstances.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(o => o.TemplateName == E2EDemoDataSeeder.DemoOffboardingTemplateName
                && otherTenantIds.Contains(o.TenantId)))
            .Should().Be(0);

        (await db.Employees.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(e => e.EmployeeNo.StartsWith("E2E-") && otherTenantIds.Contains(e.TenantId)))
            .Should().Be(0);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Boots the host (CreateClient first — the factory is lazy, and until it boots DbInitializer has not
    /// run) and returns a scoped DbContext plus the resolved <c>e2e</c> tenant id.
    /// </summary>
    private async Task<(AppDbContext Db, Guid TenantId)> ArrangeAsync()
    {
        _ = _factory.CreateClient();

        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenantId = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Subdomain == E2ESubdomain)
            .Select(t => t.Id)
            .FirstOrDefaultAsync();

        tenantId.Should().NotBe(Guid.Empty,
            $"the DEV '{E2ESubdomain}' tenant must be seeded — the factory runs in Development");

        return (db, tenantId);
    }

    private static async Task<Guid> DemoCycleIdAsync(AppDbContext db, Guid tenantId)
    {
        var id = await db.AppraisalCycles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Name == E2EDemoDataSeeder.DemoCycleName)
            .Select(c => c.Id)
            .FirstOrDefaultAsync();

        id.Should().NotBe(Guid.Empty, "the demo appraisal cycle must be seeded");
        return id;
    }

    private static async Task<Dictionary<string, int>> CountsAsync(AppDbContext db, Guid tenantId) => new()
    {
        ["employees"] = await db.Employees.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(e => e.TenantId == tenantId && e.EmployeeNo.StartsWith("E2E-")),
        ["departments"] = await db.Departments.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(d => d.TenantId == tenantId),
        ["job_titles"] = await db.JobTitles.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(j => j.TenantId == tenantId),
        ["appraisal_cycles"] = await db.AppraisalCycles.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(c => c.TenantId == tenantId && c.Name == E2EDemoDataSeeder.DemoCycleName),
        ["cycle_phases"] = await db.CyclePhases.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(p => p.TenantId == tenantId),
        ["cycle_participants"] = await db.CycleParticipants.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(p => p.TenantId == tenantId),
        ["reviewer_assignments"] = await db.ReviewerAssignments.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId),
        ["feedback_360"] = await db.Feedback360s.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(f => f.TenantId == tenantId),
        ["feedback_360_items"] = await db.Feedback360Items.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(i => i.TenantId == tenantId),
        ["offboarding_instances"] = await db.OffboardingInstances.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(o => o.TenantId == tenantId),
        ["offboarding_tasks"] = await db.OffboardingTaskInstances.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(t => t.TenantId == tenantId),
    };
}
