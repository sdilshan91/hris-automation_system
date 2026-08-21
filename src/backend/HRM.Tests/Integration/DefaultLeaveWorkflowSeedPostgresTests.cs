// ============================================================================
// GAP-029 / queue item C1 — the default leave-approval workflow seed.
//
// WHY THIS IS A BEHAVIOUR-CHANGE TEST, NOT A SEED TEST. Until now NO tenant had a workflow definition, so
// WorkflowRuntimeService's AC-11 branch returned Legacy() for every leave submission and the whole
// US-ADM-011 engine sat dormant. Seeding a definition flips real approval routing for real tenants. The
// question that matters is therefore not "did a row get written" but "does the seeded route send the
// request to the SAME person the legacy path did". These tests answer that.
//
// Postgres, not InMemory, deliberately: this is routing + FK-shaped data (definition -> steps ->
// instances), and InMemory enforces no FKs. Also matches the sibling Workflow*PostgresTests.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Persistence.Seed;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class DefaultLeaveWorkflowSeedPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    // A SECOND tenant. Without one, every arm runs single-tenant and dropping the `w.TenantId == tenantId`
    // predicate from the backfill's IgnoreQueryFilters() read is INVISIBLE -- "any Leave workflow globally"
    // and "any Leave workflow for this tenant" are the same statement when only one tenant exists. Given
    // that tenant isolation is this platform's non-negotiable rule, a reconciler whose tenant scoping no
    // test can kill is a real hole.
    private readonly Guid _otherTenantId = Guid.NewGuid();

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _managerEmployeeId = BaseEntity.NewUuidV7();
    private readonly Guid _requesterUserId = Guid.NewGuid();
    private readonly Guid _requesterEmployeeId = BaseEntity.NewUuidV7();

    private readonly MutableTenantContext _tc = new();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tc.SetTenant(_tenantId, "acme", TenantStatus.Active);

        await using var db = Db(User(_requesterUserId));
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.Tenants.Add(new Tenant { Id = _otherTenantId, Subdomain = "globex", Name = "Globex" });

        // Employee.UserId / DepartmentId / JobTitleId are all real FKs, and POSTGRES ENFORCES THEM. The
        // InMemory provider does not — an earlier draft of this test omitted the Users rows entirely and
        // would have passed on InMemory while the schema was violated. This is precisely the
        // InMemory-masks-Postgres class, caught here only because these tests run on real Postgres.
        db.Users.Add(new User { Id = _managerUserId, Email = "mia@acme.com", DisplayName = "Mia Manager" });
        db.Users.Add(new User { Id = _requesterUserId, Email = "raj@acme.com", DisplayName = "Raj Requester" });
        var departmentId = BaseEntity.NewUuidV7();
        var jobTitleId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = departmentId, TenantId = _tenantId, Name = "Ops", Code = "OPS" });
        db.JobTitles.Add(new JobTitle { Id = jobTitleId, TenantId = _tenantId, TitleName = "Analyst" });

        // The requester reports to the manager — the relationship BOTH the legacy path and a LineManager
        // workflow step resolve through. This is the whole basis of the equivalence claim.
        db.Employees.Add(new Employee
        {
            Id = _managerEmployeeId, TenantId = _tenantId, UserId = _managerUserId,
            EmployeeNo = "MGR-1", FirstName = "Mia", LastName = "Manager",
            Email = "mia@acme.com", DateOfJoining = new DateTime(2024, 1, 1),
            DepartmentId = departmentId, JobTitleId = jobTitleId,
        });
        db.Employees.Add(new Employee
        {
            Id = _requesterEmployeeId, TenantId = _tenantId, UserId = _requesterUserId,
            EmployeeNo = "EMP-1", FirstName = "Raj", LastName = "Requester",
            Email = "raj@acme.com", DateOfJoining = new DateTime(2024, 1, 1),
            DepartmentId = departmentId, JobTitleId = jobTitleId,
            ReportsToEmployeeId = _managerEmployeeId,
        });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static ICurrentUser User(Guid userId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("user@acme.com");
        return u;
    }

    private AppDbContext Db(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(_tc), new AuditInterceptor(user))
            .Options, _tc);

    private WorkflowRuntimeService Runtime(AppDbContext db, ICurrentUser user) =>
        new(db, _tc, user, NullLogger<WorkflowRuntimeService>.Instance);

    private static Dictionary<string, object?> LeaveRequestData() => new()
    {
        ["days_requested"] = 3m,
        ["is_half_day"] = false,
        ["is_lop"] = false,
    };

    // ── The baseline this change moves away from ─────────────────────────────

    /// <summary>
    /// Establishes the PRE-seed behaviour, so the later arms are measured against something real rather
    /// than asserted against a remembered description: with no definition, every submission is Legacy.
    /// </summary>
    [Fact]
    public async Task WithoutTheSeed_LeaveSubmission_FallsBackToLegacy_GAP029()
    {
        await using var db = Db(User(_requesterUserId));

        var result = await Runtime(db, User(_requesterUserId)).CreateInstanceOnSubmitAsync(
            WorkflowEntityType.Leave, Guid.NewGuid(), _requesterEmployeeId, LeaveRequestData());

        result.InstanceCreated.Should().BeFalse(
            "with no Active definition the runtime returns Legacy() — this is the dormancy GAP-029 describes");
    }

    // ── THE ARM THAT MATTERS: same approver as legacy ────────────────────────

    /// <summary>
    /// THE EQUIVALENCE ARM. The seeded route must activate a step assigned to the requester's reporting
    /// manager — the exact person the legacy single-level path routes to. If this resolved to anyone else,
    /// the seed would be silently rerouting every tenant's leave approvals.
    /// </summary>
    [Fact]
    public async Task SeededWorkflow_RoutesToTheRequestersLineManager_SameAsLegacy_GAP029()
    {
        await using (var seed = Db(User(_requesterUserId)))
        {
            seed.WorkflowDefinitions.Add(DefaultLeaveWorkflow.Build(_tenantId, DateTime.UtcNow, "test"));
            await seed.SaveChangesAsync();
        }

        var entityId = Guid.NewGuid();
        await using (var db = Db(User(_requesterUserId)))
        {
            var result = await Runtime(db, User(_requesterUserId)).CreateInstanceOnSubmitAsync(
                WorkflowEntityType.Leave, entityId, _requesterEmployeeId, LeaveRequestData());

            result.InstanceCreated.Should().BeTrue("the seeded definition is Active, so the engine takes over");
        }

        await using (var verify = Db(User(_requesterUserId)))
        {
            var steps = await verify.WorkflowStepInstances.AsNoTracking()
                .Where(s => s.StepOrder == 1).ToListAsync();

            steps.Should().ContainSingle("legacy approval is single-level, so the seeded route must be too");
            steps[0].AssignedApproverUserId.Should().Be(_managerUserId,
                "LineManager resolves to the requester's reporting manager's user — the same person the "
                + "legacy path notifies and routes to. Any other value means the seed silently rerouted "
                + "approvals away from the manager who used to receive them");
        }
    }

    /// <summary>
    /// Legacy never escalated. The seeded step sets SlaHours = 0, which the runtime reads as "no due date"
    /// — so nothing gets escalated by the recurring SLA job. A non-zero default would quietly start
    /// escalating every tenant's leave requests to somebody who never previously received them.
    /// </summary>
    [Fact]
    public async Task SeededWorkflow_SetsNoSlaDueDate_SoNothingEscalates_GAP029()
    {
        await using (var seed = Db(User(_requesterUserId)))
        {
            seed.WorkflowDefinitions.Add(DefaultLeaveWorkflow.Build(_tenantId, DateTime.UtcNow, "test"));
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(User(_requesterUserId)))
        {
            await Runtime(db, User(_requesterUserId)).CreateInstanceOnSubmitAsync(
                WorkflowEntityType.Leave, Guid.NewGuid(), _requesterEmployeeId, LeaveRequestData());
        }

        await using (var verify = Db(User(_requesterUserId)))
        {
            var step = await verify.WorkflowStepInstances.AsNoTracking().SingleAsync();
            step.SlaDueAt.Should().BeNull(
                "the legacy path had no SLA and never escalated; seeding a non-zero SlaHours would start "
                + "escalating leave requests in every tenant, which is a behaviour nobody asked for");
        }
    }

    // ── Backfill semantics ───────────────────────────────────────────────────

    /// <summary>
    /// The backfill is what stops tenants splitting into two populations by signup date. Run twice, it must
    /// leave exactly one definition — startup runs it on every boot.
    /// </summary>
    [Fact]
    public async Task Backfill_IsIdempotent_AcrossRepeatedStartups_GAP029()
    {
        await using (var db = Db(User(_requesterUserId)))
        {
            await DbInitializer.EnsureDefaultLeaveWorkflowAsync(
                db, _tenantId, NullLogger.Instance, CancellationToken.None);
            await DbInitializer.EnsureDefaultLeaveWorkflowAsync(
                db, _tenantId, NullLogger.Instance, CancellationToken.None);
        }

        await using (var verify = Db(User(_requesterUserId)))
        {
            var definitions = await verify.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking()
                .Where(w => w.TenantId == _tenantId && w.EntityType == WorkflowEntityType.Leave)
                .ToListAsync();

            definitions.Should().ContainSingle(
                "the reconciler runs on every application start; a second definition would give the runtime "
                + "two Active candidates and make approval routing depend on row order");
        }
    }

    /// <summary>
    /// A tenant whose admin is part-way through authoring a DRAFT leave workflow has expressed intent.
    /// Dropping a seeded ACTIVE definition beside it would silently win the runtime lookup and route real
    /// approvals through configuration the admin never finished.
    /// </summary>
    [Fact]
    public async Task Backfill_SkipsATenantWithADraftWorkflow_RatherThanOverridingAdminIntent_GAP029()
    {
        var draftId = BaseEntity.NewUuidV7();
        await using (var seed = Db(User(_requesterUserId)))
        {
            seed.WorkflowDefinitions.Add(new WorkflowDefinition
            {
                Id = draftId, TenantId = _tenantId, Name = "Half-written custom flow",
                EntityType = WorkflowEntityType.Leave, LineageId = draftId, Version = 1,
                Status = WorkflowStatus.Draft, IsActive = false,
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(User(_requesterUserId)))
        {
            await DbInitializer.EnsureDefaultLeaveWorkflowAsync(
                db, _tenantId, NullLogger.Instance, CancellationToken.None);
        }

        await using (var verify = Db(User(_requesterUserId)))
        {
            var definitions = await verify.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking()
                .Where(w => w.TenantId == _tenantId && w.EntityType == WorkflowEntityType.Leave)
                .ToListAsync();

            definitions.Should().ContainSingle("the admin's draft must be the only definition");
            definitions[0].Id.Should().Be(draftId);
            definitions[0].Status.Should().Be(WorkflowStatus.Draft,
                "seeding an Active definition next to an unfinished draft would take over live approval "
                + "routing using config the admin never completed");
        }
    }

    /// <summary>
    /// TENANT-SCOPING ARM. Tenant A already has a Leave definition; tenant B has none. Backfilling B must
    /// give B its own definition and leave A untouched.
    ///
    /// WHY IT IS NEEDED: the backfill reads with <c>IgnoreQueryFilters()</c> — deliberately, because startup
    /// iterates tenants with no <c>ITenantContext</c> resolved — and re-scopes with an explicit
    /// <c>w.TenantId == tenantId</c> predicate. Dropping that predicate passes every single-tenant arm,
    /// because with one tenant "any Leave workflow" and "this tenant's Leave workflow" are the same
    /// question. Here they are different questions: A's definition would satisfy a global <c>Any</c> and
    /// wrongly cause B to be skipped, leaving B silently on the legacy path forever.
    ///
    /// It also pins the WRITE side: the row must land under B's TenantId even though no tenant context is
    /// resolved, which is what the builder's explicit TenantId (and TenantInterceptor's no-op when
    /// unresolved) guarantee.
    /// </summary>
    [Fact]
    public async Task Backfill_IsScopedPerTenant_AndDoesNotLeakAcrossThem_GAP029()
    {
        // Tenant A: already has a definition, so the backfill must skip it.
        // Its ID is the stable identity to assert on -- CreatedBy is NOT, because AuditInterceptor stamps it
        // with the acting user and overwrites whatever the builder set. (Learned by asserting on CreatedBy
        // first and watching it come back as a user Guid.)
        var tenantAdefinitionId = Guid.Empty;
        await using (var seed = Db(User(_requesterUserId)))
        {
            var existing = DefaultLeaveWorkflow.Build(_tenantId, DateTime.UtcNow, "pre-existing");
            tenantAdefinitionId = existing.Id;
            seed.WorkflowDefinitions.Add(existing);
            await seed.SaveChangesAsync();
        }

        // Backfill tenant B, which has none.
        await using (var db = Db(User(_requesterUserId)))
        {
            await DbInitializer.EnsureDefaultLeaveWorkflowAsync(
                db, _otherTenantId, NullLogger.Instance, CancellationToken.None);
        }

        await using (var verify = Db(User(_requesterUserId)))
        {
            var all = await verify.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking()
                .Where(w => w.EntityType == WorkflowEntityType.Leave)
                .ToListAsync();

            all.Where(w => w.TenantId == _otherTenantId).Should().ContainSingle(
                "tenant B had no Leave definition, so the backfill must create exactly one FOR B — if the "
                + "read's tenant predicate were dropped, tenant A's definition would satisfy it and B would "
                + "be skipped, stranding B on the legacy path with nothing to show why");

            all.Where(w => w.TenantId == _tenantId).Should().ContainSingle(
                "tenant A already had one; the backfill must not add a second");
            all.Single(w => w.TenantId == _tenantId).Id.Should().Be(tenantAdefinitionId,
                "tenant A must still hold ITS OWN definition row — not one the backfill replaced it with");
        }
    }
}
