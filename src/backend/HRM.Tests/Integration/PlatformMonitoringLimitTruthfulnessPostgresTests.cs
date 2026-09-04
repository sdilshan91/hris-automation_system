// ============================================================================
// BUG-473 + the precedence inversion — the System Admin usage dashboard must tell the truth about a cap.
//
// TWO defects, one theme: the console reported something the system does not do.
//
//  (1) BUG-473. PlatformMonitoringService.ResolveLongLimit called PlanLimitResolver.Resolve(...).Value
//      DIRECTLY, throwing away PlanExists — the single distinction PlanLimitLookup exists to preserve. So a
//      tenant whose plan_id matched no plan rendered as "unlimited" on the dashboard while every enforcement
//      gate returned 403 for that same tenant. An operator investigating those 403s would consult the console
//      and be told there is no cap at all.
//
//  (2) The precedence was INVERTED. Enforcement resolves override > plan > snapshot; monitoring resolved
//      `t.MaxEmployees ?? plan?.MaxEmployees` — snapshot first, plan as fallback. Unlike a staleness bug this
//      needs no staleness: a snapshot that merely DIFFERS from the plan is enough to make the two disagree.
//
// SHAPE. Neither arm asserts that monitoring returns some number the test author wrote down. The inversion arm
// DISCOVERS the enforced cap by driving the real EmployeeService until it refuses, then asserts the dashboard
// reports that same number — so the test measures agreement between the two paths, which is the property that
// was broken. A hardcoded expectation would have passed against either precedence with the right fixture.
//
// HARNESS = real Postgres via Testcontainers, schema by MigrateAsync (not InMemory): monitoring runs
// cross-tenant with IgnoreQueryFilters + GroupBy while enforcement runs tenant-scoped through the global query
// filter, and the whole point is that both see the same rows. InMemory does not model filter translation
// faithfully enough for an agreement claim to mean anything.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Monitoring.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-ADM-012-473")]
[Trait("Category", "Monitoring")]
public sealed class PlatformMonitoringLimitTruthfulnessPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private string _cs = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _cs = _postgres.GetConnectionString() + ";Include Error Detail=true";
        await using var db = SystemDb();
        await db.Database.MigrateAsync();

        // Plans ARE configured for this deployment. That matters: PlanLimitLookup deliberately treats a
        // deployment with NO plans as "not using plan-based limiting", so an unresolvable plan_id is only a
        // configuration error while some plan exists to have been resolved against.
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = BaseEntity.NewUuidV7(), Code = "starter", Name = "Starter", MaxEmployees = 25, MaxStorageGb = 10,
        });
        // A DELIBERATE unlimited: the plan row exists and says "no cap". This is the control that stops the
        // BUG-473 arm from passing for the wrong reason — without it, a fix that reported ConfigurationError
        // unconditionally would look correct.
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = BaseEntity.NewUuidV7(), Code = "enterprise", Name = "Enterprise", MaxEmployees = null,
            MaxStorageGb = null,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── BUG-473: an unresolvable plan_id must NOT render as "unlimited" ──────────────────────────────────

    /// <summary>
    /// The gauge arm. A broken <c>plan_id</c> and a genuinely uncapped plan BOTH produce a null Limit — they
    /// always did, and that is fine. What must differ is what the dashboard SAYS about them, because the
    /// enforcement gates already treat them as opposites (403 vs allow).
    /// </summary>
    [Fact]
    public async Task Usage_UnresolvablePlanId_ReportsConfigurationError_NotUnlimited_BUG473()
    {
        var brokenId = await SeedTenantAsync("broken", planId: "no-such-plan", snapshotMaxEmployees: null);
        var unlimitedId = await SeedTenantAsync("unltd", planId: "enterprise", snapshotMaxEmployees: null);

        var result = await Monitoring().GetTenantUsageAsync(new TenantUsageFilter());
        result.IsSuccess.Should().BeTrue(result.Error);

        var broken = result.Value!.Tenants.Single(t => t.TenantId == brokenId);
        var unlimited = result.Value.Tenants.Single(t => t.TenantId == unlimitedId);

        broken.LimitStatus.Should().Be(PlanLimitStatus.ConfigurationError,
            "the tenant's plan_id matches no configured plan, and the enforcement gates fail CLOSED on exactly "
            + "that state — the dashboard must not describe it as an allowance");
        broken.LimitStatus.Should().NotBe(PlanLimitStatus.Unlimited,
            "reporting 'unlimited' for a broken plan_id is BUG-473 itself: it points an operator investigating "
            + "403s away from the cause");
        broken.EmployeeLimit.Should().BeNull();

        // The control. Same null Limit, opposite meaning — so the status is carrying real information rather
        // than being a constant.
        unlimited.LimitStatus.Should().Be(PlanLimitStatus.Unlimited,
            "a resolvable plan that sets max_employees = NULL is a deliberate 'no cap' (BR-3), and must keep "
            + "reading as unlimited");
        unlimited.EmployeeLimit.Should().BeNull();

        // Every gauge on the broken row, not just the headline one — all four went through the same discarded
        // PlanExists, so all four claimed "unlimited".
        broken.Gauges.Should().OnlyContain(g => g.LimitStatus == PlanLimitStatus.ConfigurationError,
            "all four gauges resolved their cap through the same lookup that discarded PlanExists");
        unlimited.Gauges.Should().OnlyContain(g => g.LimitStatus == PlanLimitStatus.Unlimited);
    }

    /// <summary>
    /// The per-tenant DETAIL view resolves limits on its own code path. It carried an identical copy of the
    /// bug, so proving the dashboard sweep says nothing about it.
    /// </summary>
    [Fact]
    public async Task TenantDetail_UnresolvablePlanId_ReportsConfigurationError_NotUnlimited_BUG473()
    {
        var brokenId = await SeedTenantAsync("dbroken", planId: "no-such-plan", snapshotMaxEmployees: null);
        var unlimitedId = await SeedTenantAsync("dunltd", planId: "enterprise", snapshotMaxEmployees: null);

        var broken = await Monitoring().GetTenantDetailAsync(brokenId);
        var unlimited = await Monitoring().GetTenantDetailAsync(unlimitedId);

        broken.IsSuccess.Should().BeTrue(broken.Error);
        unlimited.IsSuccess.Should().BeTrue(unlimited.Error);

        broken.Value!.EmployeeUsage.LimitStatus.Should().Be(PlanLimitStatus.ConfigurationError);
        broken.Value.EmployeeUsage.Limit.Should().BeNull();
        unlimited.Value!.EmployeeUsage.LimitStatus.Should().Be(PlanLimitStatus.Unlimited);
    }

    /// <summary>
    /// Closes the loop the finding is actually about: the dashboard and the enforcement gate must now agree
    /// that this tenant is misconfigured. Before the fix one said "unlimited" and the other said 403.
    /// </summary>
    [Fact]
    public async Task Usage_UnresolvablePlanId_AgreesWithEnforcementWhichRefuses_BUG473()
    {
        var tenantId = await SeedTenantAsync("agree", planId: "no-such-plan", snapshotMaxEmployees: null);
        var (deptId, jobTitleId) = await SeedOrgAsync(tenantId);

        var enforcement = await CreateEmployeeAsync(tenantId, deptId, jobTitleId, "first@agree.test");

        enforcement.IsSuccess.Should().BeFalse(
            "the enforcement gate fails closed on an unresolvable plan_id (BUG-307)");
        enforcement.StatusCode.Should().Be(403);

        var row = (await Monitoring().GetTenantUsageAsync(new TenantUsageFilter()))
            .Value!.Tenants.Single(t => t.TenantId == tenantId);

        row.LimitStatus.Should().Be(PlanLimitStatus.ConfigurationError,
            "the dashboard must describe the same state the gate just enforced — a console that contradicts "
            + "the system's own enforcement is worse than no console");
    }

    // ── The precedence inversion: monitoring and enforcement must return the SAME cap ────────────────────

    /// <summary>
    /// THE arm for the inversion. The tenant's snapshot (99) differs from its plan (3) — no staleness, just a
    /// difference, which is all the inverted precedence ever needed. Enforcement resolves plan-first and caps
    /// at 3; monitoring resolved snapshot-first and would advertise 99, i.e. a tenant sitting at 100% of its
    /// real cap displayed as ~3% and comfortably green.
    ///
    /// <para>The enforced cap is DISCOVERED, not asserted: employees are created through the real
    /// <c>EmployeeService</c> until the gate refuses, and the number that got through IS the cap the system
    /// applies. The dashboard is then required to report that number. Nothing here would still pass if the two
    /// paths silently agreed on the wrong value together, because the enforced side is measured behaviour.</para>
    /// </summary>
    [Fact]
    public async Task Usage_SnapshotDiffersFromPlan_DashboardReportsTheSameCapEnforcementApplies()
    {
        // Plan says 3, the denormalized snapshot says 99. Enforcement prefers the plan; monitoring preferred
        // the snapshot.
        var tenantId = await SeedTenantAsync("inverted", planId: "tight", snapshotMaxEmployees: 99, seedPlan: p =>
        {
            p.Code = "tight";
            p.Name = "Tight";
            p.MaxEmployees = 3;
        });
        var (deptId, jobTitleId) = await SeedOrgAsync(tenantId);

        // Discover the ENFORCED cap empirically.
        var enforcedCap = 0;
        var refused = false;
        for (var i = 0; i < 20 && !refused; i++)
        {
            var attempt = await CreateEmployeeAsync(tenantId, deptId, jobTitleId, $"e{i}@inverted.test");
            if (attempt.IsSuccess)
            {
                enforcedCap++;
                continue;
            }

            attempt.StatusCode.Should().Be(403, $"the refusal must be the plan-limit gate. Error: {attempt.Error}");
            refused = true;
        }

        refused.Should().BeTrue(
            "the enforced cap must actually be reachable within 20 creates, otherwise this test measures nothing");

        var row = (await Monitoring().GetTenantUsageAsync(new TenantUsageFilter()))
            .Value!.Tenants.Single(t => t.TenantId == tenantId);

        row.EmployeeLimit.Should().Be(enforcedCap,
            "there must be ONE answer to 'what is this tenant's cap'. The dashboard prefers the plan exactly "
            + "as every enforcement gate does; preferring the denormalized snapshot let the console advertise "
            + "a ceiling the system refuses to honour");
        row.ActiveEmployees.Should().Be(enforcedCap);
        row.UsagePercent.Should().Be(100d,
            "a tenant that enforcement will not let grow must render as full, not as comfortably under a "
            + "snapshot-derived ceiling that does not exist");
        row.LimitStatus.Should().Be(PlanLimitStatus.Enforced);
    }

    /// <summary>
    /// The snapshot is still a legitimate FALLBACK — this is what stops the fix from being "ignore the
    /// snapshot", which would break tenants provisioned before their plan defined the limit. Same rule as
    /// <c>EffectivePlanLimit.WithSnapshotFallback</c>: plan value wins, snapshot fills a plan-side null.
    /// </summary>
    [Fact]
    public async Task Usage_PlanDefinesNoCapButTenantSnapshotDoes_TheSnapshotIsStillUsed()
    {
        var tenantId = await SeedTenantAsync("fallback", planId: "enterprise", snapshotMaxEmployees: 42);

        var row = (await Monitoring().GetTenantUsageAsync(new TenantUsageFilter()))
            .Value!.Tenants.Single(t => t.TenantId == tenantId);

        row.EmployeeLimit.Should().Be(42,
            "the plan resolves but sets no employee cap, so the tenant's snapshot is the fallback — dropping "
            + "it would silently uncap every tenant provisioned before the plan defined the limit");
        row.LimitStatus.Should().Be(PlanLimitStatus.Enforced,
            "a cap that came from the snapshot is still a real cap, not 'unlimited'");
    }

    // ── seeding / harness ────────────────────────────────────────────────────

    private async Task<Guid> SeedTenantAsync(
        string subdomain, string planId, int? snapshotMaxEmployees, Action<SubscriptionPlan>? seedPlan = null)
    {
        var id = Guid.NewGuid();
        await using var db = SystemDb();

        if (seedPlan is not null)
        {
            var plan = new SubscriptionPlan { Id = BaseEntity.NewUuidV7(), Code = planId, Name = planId };
            seedPlan(plan);
            db.SubscriptionPlans.Add(plan);
        }

        db.Tenants.Add(new Tenant
        {
            Id = id,
            Name = subdomain,
            Subdomain = subdomain,
            Status = TenantStatus.Active,
            PlanId = planId,
            MaxEmployees = snapshotMaxEmployees,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<(Guid DepartmentId, Guid JobTitleId)> SeedOrgAsync(Guid tenantId)
    {
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        await using var db = SystemDb();
        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = tenantId, Name = "Eng", Code = $"E{tenantId:N}"[..8], IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = "Engineer", IsActive = true,
        });
        await db.SaveChangesAsync();
        return (deptId, jobTitleId);
    }

    /// <summary>Drives the REAL employee-creation gate, tenant-scoped, so the cap it applies is observed rather than assumed.</summary>
    private async Task<Result<EmployeeDto>> CreateEmployeeAsync(
        Guid tenantId, Guid deptId, Guid jobTitleId, string email)
    {
        var tenantContext = new ScopedTenantContext(tenantId);
        await using var db = ScopedDb(tenantContext);

        var hr = Substitute.For<ICurrentUser>();
        hr.IsAuthenticated.Returns(true);
        hr.UserId.Returns(Guid.NewGuid());
        hr.Email.Returns("hr@test.local");

        var customFields = Substitute.For<ICustomFieldService>();
        customFields
            .ValidateCustomFieldValuesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var service = new EmployeeService(
            db, tenantContext, hr, Substitute.For<IFileStorage>(), Substitute.For<IVirusScanner>(),
            customFields, Substitute.For<IPayrollAuditLogger>(), NullLogger<EmployeeService>.Instance);

        return await service.CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "T",
            LastName = "U",
            Email = email,
            DateOfJoining = DateTime.UtcNow,
            DepartmentId = deptId,
            JobTitleId = jobTitleId,
            EmploymentType = EmploymentType.FullTime,
        });
    }

    private PlatformMonitoringService Monitoring()
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        user.IsAuthenticated.Returns(true);
        var jobQueue = Substitute.For<IJobQueueMonitor>();
        jobQueue.GetSnapshot().Returns(new JobQueueSnapshotDto(false, null, null, null, null, null));
        return new PlatformMonitoringService(
            SystemDb(), user, jobQueue, new ConfigurationBuilder().Build(),
            NullLogger<PlatformMonitoringService>.Instance);
    }

    private DbContextOptions<AppDbContext> Options(ITenantContext tenantContext, bool withInterceptors)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_cs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        if (withInterceptors)
        {
            var user = Substitute.For<ICurrentUser>();
            user.UserId.Returns(Guid.NewGuid());
            builder = builder.AddInterceptors(new TenantInterceptor(tenantContext), new AuditInterceptor(user));
        }

        return builder.Options;
    }

    /// <summary>No resolved tenant ⇒ query filters disabled ⇒ the cross-tenant monitoring reads apply.</summary>
    private AppDbContext SystemDb() => new(Options(new SystemTenantContext(), withInterceptors: false),
        new SystemTenantContext());

    /// <summary>Resolved to ONE tenant, so enforcement runs behind the real global query filter.</summary>
    private AppDbContext ScopedDb(ITenantContext tenantContext) =>
        new(Options(tenantContext, withInterceptors: true), tenantContext);

    private sealed class ScopedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public string Subdomain => "scoped";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => true;
        public void SetTenant(Guid t, string s, TenantStatus st, string? p = null,
            IReadOnlyCollection<string>? m = null, string? l = null, string? c = null) { }
        public void SetSystemContext() { }
    }

    private sealed class SystemTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string Subdomain => "admin";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => true;
        public bool IsResolved => false;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }
}
