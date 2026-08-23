// ============================================================================
// US-TRN-003 — benefit enrollment against real Postgres (Testcontainers). Exercises the ordered enrollment gate
// (plan Active → window → eligibility → duplicate), termination, the eligible-plans query, audit rows, best-effort
// notification dispatch, and EF global-query-filter tenant isolation for both new tables. Schema is built with
// EnsureCreatedAsync() (mirrors the other *PostgresTests). RLS stays dormant (flag off) — isolation here is the EF
// global query filter + the partial unique index for duplicate safety.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Benefits.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-TRN-003")]
public sealed class BenefitEnrollmentPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    // Tenant A employees + their linked users.
    private readonly Guid _userFullTime = Guid.NewGuid();
    private readonly Guid _empFullTime = Guid.NewGuid();
    private readonly Guid _userContract = Guid.NewGuid();
    private readonly Guid _empContract = Guid.NewGuid();

    private readonly Guid _deptEng = Guid.NewGuid();
    private readonly Guid _jobSenior = Guid.NewGuid();

    // Tenant B (isolation).
    private readonly Guid _userB = Guid.NewGuid();
    private readonly Guid _empB = Guid.NewGuid();
    private readonly Guid _deptB = Guid.NewGuid();
    private readonly Guid _jobB = Guid.NewGuid();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme", Currency = "USD" });
        db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex", Currency = "USD" });

        db.Users.Add(new User { Id = _userFullTime, Email = "ft@acme.com" });
        db.Users.Add(new User { Id = _userContract, Email = "ct@acme.com" });
        db.Users.Add(new User { Id = _userB, Email = "b@globex.com" });

        // Reference data (Employee has required FKs to Department + JobTitle).
        db.Departments.Add(new Department { Id = _deptEng, TenantId = _tenantA, Name = "Engineering", Code = "ENG" });
        db.JobTitles.Add(new JobTitle { Id = _jobSenior, TenantId = _tenantA, TitleName = "Senior Engineer" });
        db.Departments.Add(new Department { Id = _deptB, TenantId = _tenantB, Name = "Ops", Code = "OPS" });
        db.JobTitles.Add(new JobTitle { Id = _jobB, TenantId = _tenantB, TitleName = "Operator" });

        db.Employees.Add(Employee(_empFullTime, _tenantA, _userFullTime, EmploymentType.FullTime, 200, _deptEng, _jobSenior, "EMP-FT"));
        db.Employees.Add(Employee(_empContract, _tenantA, _userContract, EmploymentType.Contract, 200, _deptEng, _jobSenior, "EMP-CT"));
        db.Employees.Add(Employee(_empB, _tenantB, _userB, EmploymentType.FullTime, 200, _deptB, _jobB, "EMP-B"));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── AC-3: enroll eligible + in-window → Active + audit + notify ──────────────

    [Fact]
    public async Task Enroll_EligibleInWindow_CreatesActive_AuditAndNotify()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active, opensAt: Today.AddDays(-5), closesAt: Today.AddDays(5));
        await SeedRuleAsync(_tenantA, planId, EligibilityAttribute.EmploymentType, "==", "FullTime");

        var dispatcher = Substitute.For<INotificationDispatcher>();
        await using var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var service = Service(db, _userFullTime, dispatcher);

        var result = await service.EnrollAsync(
            new EnrollRequest { PlanId = planId, CoverageLevel = "Family" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Active");
        result.Value.CoverageLevel.Should().Be("Family");
        result.Value.EmployeeId.Should().Be(_empFullTime);

        await using var verify = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var persisted = await verify.BenefitEnrollments.AsNoTracking().SingleAsync(e => e.Id == result.Value.Id);
        persisted.Status.Should().Be(BenefitEnrollmentStatus.Active);

        var audit = await verify.AuditLogs.AsNoTracking()
            .Where(l => l.EventType == "Benefits.Enrolled" && l.ResourceId == result.Value.Id.ToString())
            .ToListAsync();
        audit.Should().ContainSingle();

        await dispatcher.Received(1).SendInAppAsync(Arg.Is<NotificationRequest>(r => r.EventKey == "benefit_enrolled"), Arg.Any<CancellationToken>());
        await dispatcher.Received(1).SendEmailAsync(Arg.Is<NotificationRequest>(r => r.EventKey == "benefit_enrolled"), Arg.Any<CancellationToken>());
    }

    // ── GAP-026 / C4: employment status gates NEW enrollments ────────────────────
    //
    // Status was never consulted on this path, so a TERMINATED employee could be enrolled into any
    // rules-free plan — silently, with no error and no log, on a benefits-cost path. Note the plan in these
    // arms carries NO eligibility rules on purpose: with rules present, an unrelated rule could refuse the
    // enrollment and the arm would pass without the status guard existing at all.

    [Theory]
    [InlineData(EmployeeStatus.Terminated)]
    [InlineData(EmployeeStatus.Inactive)]
    public async Task Enroll_EmployeeWhoHasLeft_Is422_AndWritesNoRow_gap026(EmployeeStatus status)
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active,
            opensAt: Today.AddDays(-5), closesAt: Today.AddDays(5));
        await SetEmployeeStatusAsync(_empFullTime, status);

        await using var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var result = await Service(db, _userFullTime).EnrollAsync(
            new EnrollRequest { PlanId = planId, CoverageLevel = "EmployeeOnly" }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("employee_not_enrollable");

        await using var verify = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        (await verify.BenefitEnrollments.AsNoTracking().CountAsync(e => e.EmployeeId == _empFullTime))
            .Should().Be(0, "a refused enrollment must leave no row behind");

        await SetEmployeeStatusAsync(_empFullTime, EmployeeStatus.Active);
    }

    /// <summary>
    /// THE ARM THAT STOPS THE FIX OVER-REACHING. The gap register prescribed <c>Status == Active</c>, which
    /// would have blocked probationary and suspended employees too — a regression dressed as a fix. A
    /// probationer is employed and routinely enrols; a suspended employee is still employed and their cover
    /// normally continues. Plan-specific exclusions are what the eligibility RULES are for.
    /// </summary>
    [Theory]
    [InlineData(EmployeeStatus.Probation)]
    [InlineData(EmployeeStatus.Suspended)]
    public async Task Enroll_EmployeeStillEmployed_Succeeds_gap026(EmployeeStatus status)
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active,
            opensAt: Today.AddDays(-5), closesAt: Today.AddDays(5));
        await SetEmployeeStatusAsync(_empFullTime, status);

        await using var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var result = await Service(db, _userFullTime).EnrollAsync(
            new EnrollRequest { PlanId = planId, CoverageLevel = "EmployeeOnly" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            $"{status} is still employed — blocking it would be a regression, not a fix. Error: {result.Error}");

        await SetEmployeeStatusAsync(_empFullTime, EmployeeStatus.Active);
    }

    /// <summary>
    /// C4's decision: block NEW enrollments, leave EXISTING ones alone. AC-7 makes ending an enrollment an
    /// explicit manual act, and a validation guard must not silently terminate live benefit records as a
    /// side effect of a deploy — someone terminated mid-year keeps cover until a human ends it.
    /// </summary>
    [Fact]
    public async Task Termination_does_not_disturb_an_existing_enrollment_gap026()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active,
            opensAt: Today.AddDays(-5), closesAt: Today.AddDays(5));

        await using (var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA)))
        {
            (await Service(db, _userFullTime).EnrollAsync(
                new EnrollRequest { PlanId = planId, CoverageLevel = "EmployeeOnly" }, CancellationToken.None))
                .IsSuccess.Should().BeTrue();
        }

        await SetEmployeeStatusAsync(_empFullTime, EmployeeStatus.Terminated);

        await using var verify = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var enrollment = await verify.BenefitEnrollments.AsNoTracking()
            .SingleAsync(e => e.EmployeeId == _empFullTime && e.BenefitPlanId == planId);
        enrollment.Status.Should().Be(BenefitEnrollmentStatus.Active,
            "the guard blocks new enrollments; it must not retroactively end cover someone already has");

        await SetEmployeeStatusAsync(_empFullTime, EmployeeStatus.Active);
    }

    [Fact]
    public async Task EligiblePlans_AreEmpty_ForAnEmployeeWhoHasLeft_gap026()
    {
        await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active,
            opensAt: Today.AddDays(-5), closesAt: Today.AddDays(5));

        await using (var before = Db(Tc(_tenantA), User(_userFullTime, _tenantA)))
        {
            (await Service(before, _userFullTime).GetMyEligiblePlansAsync(CancellationToken.None))
                .Value!.Should().NotBeEmpty("the arm is vacuous unless the plan is eligible while employed");
        }

        await SetEmployeeStatusAsync(_empFullTime, EmployeeStatus.Terminated);

        await using var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var plans = await Service(db, _userFullTime).GetMyEligiblePlansAsync(CancellationToken.None);

        plans.IsSuccess.Should().BeTrue();
        plans.Value!.Should().BeEmpty(
            "listing plans the enrollment endpoint would refuse is the same defect one screen later — and it "
            + "is how HR ends up believing a terminated employee is still covered");

        await SetEmployeeStatusAsync(_empFullTime, EmployeeStatus.Active);
    }

    private async Task SetEmployeeStatusAsync(Guid employeeId, EmployeeStatus status)
    {
        await using var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var employee = await db.Employees.SingleAsync(e => e.Id == employeeId);
        employee.Status = status;
        await db.SaveChangesAsync();
    }

    // ── AC-4: ineligible → 422 not_eligible + failing-rule reason ────────────────

    [Fact]
    public async Task Enroll_Ineligible_Returns422_WithReason_AndNoRow()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active);
        await SeedRuleAsync(_tenantA, planId, EligibilityAttribute.EmploymentType, "==", "FullTime");

        await using var db = Db(Tc(_tenantA), User(_userContract, _tenantA));
        var service = Service(db, _userContract);

        var result = await service.EnrollAsync(new EnrollRequest { PlanId = planId, CoverageLevel = "EmployeeOnly" }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("not_eligible");
        result.Error.Should().Contain("EmploymentType");

        await using var verify = Db(Tc(_tenantA), User(_userContract, _tenantA));
        (await verify.BenefitEnrollments.AsNoTracking().AnyAsync(e => e.EmployeeId == _empContract)).Should().BeFalse();
    }

    // ── AC-5: duplicate active → 409 already_enrolled ────────────────────────────

    [Fact]
    public async Task Enroll_DuplicateActive_Returns409()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active);

        await using (var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA)))
        {
            var first = await Service(db, _userFullTime).EnrollAsync(
                new EnrollRequest { PlanId = planId, CoverageLevel = "EmployeeOnly" }, CancellationToken.None);
            first.IsSuccess.Should().BeTrue();
        }

        await using var db2 = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var second = await Service(db2, _userFullTime).EnrollAsync(
            new EnrollRequest { PlanId = planId, CoverageLevel = "EmployeeOnly" }, CancellationToken.None);

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("already_enrolled");
    }

    // ── AC-6: window closed → 422 enrollment_window_closed ───────────────────────

    [Fact]
    public async Task Enroll_WindowClosed_Returns422()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active,
            opensAt: Today.AddDays(-30), closesAt: Today.AddDays(-1));

        await using var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var result = await Service(db, _userFullTime).EnrollAsync(
            new EnrollRequest { PlanId = planId, CoverageLevel = "EmployeeOnly" }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("enrollment_window_closed");
    }

    // ── plan not Active → 409 plan_not_active ────────────────────────────────────

    [Fact]
    public async Task Enroll_PlanNotActive_Returns409()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Draft);

        await using var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var result = await Service(db, _userFullTime).EnrollAsync(
            new EnrollRequest { PlanId = planId, CoverageLevel = "EmployeeOnly" }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("plan_not_active");
    }

    // ── AC-7: terminate → Terminated + EndDate + audit ───────────────────────────

    [Fact]
    public async Task Terminate_SetsTerminated_EndDate_AndAudit()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active);

        Guid enrollmentId;
        await using (var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA)))
        {
            var enroll = await Service(db, _userFullTime).EnrollAsync(
                new EnrollRequest { PlanId = planId, CoverageLevel = "EmployeeOnly" }, CancellationToken.None);
            enrollmentId = enroll.Value!.Id;
        }

        var endDate = Today.AddDays(3);
        await using var db2 = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var result = await Service(db2, _userFullTime).TerminateAsync(
            enrollmentId, new TerminateEnrollmentRequest { EndDate = endDate }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Terminated");
        result.Value.EndDate.Should().Be(endDate);

        await using var verify = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var persisted = await verify.BenefitEnrollments.AsNoTracking().SingleAsync(e => e.Id == enrollmentId);
        persisted.Status.Should().Be(BenefitEnrollmentStatus.Terminated);
        persisted.EndDate.Should().Be(endDate);

        (await verify.AuditLogs.AsNoTracking()
            .AnyAsync(l => l.EventType == "Benefits.EnrollmentTerminated" && l.ResourceId == enrollmentId.ToString()))
            .Should().BeTrue();
    }

    // ── AC-2: eligible-plans returns only Active+effective+in-window+passing ─────

    [Fact]
    public async Task EligiblePlans_ReturnsOnlyQualifyingPlans()
    {
        // Qualifying: Active, in-window, rule passes (FullTime).
        var pass = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active, opensAt: Today.AddDays(-1), closesAt: Today.AddDays(30));
        await SeedRuleAsync(_tenantA, pass, EligibilityAttribute.EmploymentType, "==", "FullTime");

        // Excluded: window closed.
        var closed = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active, opensAt: Today.AddDays(-30), closesAt: Today.AddDays(-1));

        // Excluded: not Active.
        var draft = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Draft);

        // Excluded: rule fails (requires Contract).
        var ruleFails = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active);
        await SeedRuleAsync(_tenantA, ruleFails, EligibilityAttribute.EmploymentType, "==", "Contract");

        await using var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var result = await Service(db, _userFullTime).GetMyEligiblePlansAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var ids = result.Value!.Select(p => p.PlanId).ToList();
        ids.Should().Contain(pass);
        ids.Should().NotContain(closed).And.NotContain(draft).And.NotContain(ruleFails);
        result.Value!.Single(p => p.PlanId == pass).EnrollmentOpen.Should().BeTrue();
    }

    // ── AC-9: tenant isolation — rules + enrollments do not leak ─────────────────

    [Fact]
    public async Task TenantIsolation_RulesAndEnrollmentsDoNotLeak()
    {
        // Tenant B: a plan with a rule and an enrollment.
        var planB = await SeedPlanAsync(_tenantB, BenefitPlanStatus.Active);
        await SeedRuleAsync(_tenantB, planB, EligibilityAttribute.EmploymentType, "==", "FullTime");
        await using (var dbB = Db(Tc(_tenantB), User(_userB, _tenantB)))
        {
            var enrollB = await Service(dbB, _userB).EnrollAsync(
                new EnrollRequest { PlanId = planB, CoverageLevel = "EmployeeOnly" }, CancellationToken.None);
            enrollB.IsSuccess.Should().BeTrue();
        }

        await using var dbA = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var serviceA = Service(dbA, _userFullTime);

        // Tenant A cannot read Tenant B's rules (plan is filtered out → 404).
        var rules = await serviceA.GetRulesAsync(planB, CancellationToken.None);
        rules.IsFailure.Should().BeTrue();
        rules.StatusCode.Should().Be(404);

        // Tenant A cannot see Tenant B's enrollments.
        var enrollmentsB = await serviceA.GetEmployeeEnrollmentsAsync(_empB, CancellationToken.None);
        enrollmentsB.Value.Should().BeEmpty();

        // Direct table read from Tenant A's context never returns Tenant B rows.
        (await dbA.BenefitEnrollments.AsNoTracking().AnyAsync(e => e.BenefitPlanId == planB)).Should().BeFalse();
        (await dbA.BenefitEligibilityRules.AsNoTracking().AnyAsync(r => r.BenefitPlanId == planB)).Should().BeFalse();
    }

    // ── Rule CRUD: add persists + audits; invalid → 400 invalid_rule ─────────────

    [Fact]
    public async Task AddRule_Persists_AndAudits()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active);

        await using var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var service = Service(db, _userFullTime);

        var result = await service.AddRuleAsync(planId, new CreateEligibilityRuleRequest
        {
            Attribute = "TenureDays", Operator = ">=", Value = "90",
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Attribute.Should().Be("TenureDays");

        await using var verify = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        (await verify.BenefitEligibilityRules.AsNoTracking().AnyAsync(r => r.Id == result.Value.Id)).Should().BeTrue();
        (await verify.AuditLogs.AsNoTracking().AnyAsync(l => l.EventType == "Benefits.EligibilityRuleAdded")).Should().BeTrue();
    }

    [Fact]
    public async Task AddRule_Invalid_Returns400_InvalidRule()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active);

        await using var db = Db(Tc(_tenantA), User(_userFullTime, _tenantA));
        var service = Service(db, _userFullTime);

        var result = await service.AddRuleAsync(planId, new CreateEligibilityRuleRequest
        {
            Attribute = "TenureDays", Operator = ">=", Value = "not-a-number",
        }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("invalid_rule");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedPlanAsync(
        Guid tenantId, BenefitPlanStatus status,
        DateOnly? opensAt = null, DateOnly? closesAt = null)
    {
        var id = BaseEntity.NewUuidV7();
        await using var db = Db(Tc(tenantId), User(_userFullTime, tenantId));
        db.BenefitPlans.Add(new BenefitPlan
        {
            Id = id, TenantId = tenantId, Name = "Plan " + status, Type = BenefitType.Health,
            Currency = "USD", EffectiveFrom = Today.AddDays(-10), Status = status,
            EnrollmentOpensAt = opensAt, EnrollmentClosesAt = closesAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedRuleAsync(Guid tenantId, Guid planId, EligibilityAttribute attr, string op, string value)
    {
        await using var db = Db(Tc(tenantId), User(_userFullTime, tenantId));
        db.BenefitEligibilityRules.Add(new BenefitEligibilityRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, BenefitPlanId = planId,
            Attribute = attr, Operator = op, Value = value,
        });
        await db.SaveChangesAsync();
    }

    private static Employee Employee(
        Guid id, Guid tenantId, Guid userId, EmploymentType type, int tenureDays, Guid deptId, Guid jobId, string empNo) => new()
    {
        Id = id, TenantId = tenantId, UserId = userId, EmployeeNo = empNo,
        FirstName = "Test", LastName = empNo, Email = empNo + "@acme.com",
        DateOfJoining = DateTime.UtcNow.AddDays(-tenureDays), EmploymentType = type,
        DepartmentId = deptId, JobTitleId = jobId, Status = EmployeeStatus.Active,
    };

    private AppDbContext Db(ITenantContext tc, ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .EnableDetailedErrors()
            .UseNpgsql(_postgres.GetConnectionString() + ";Include Error Detail=true", n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(user))
            .Options, tc);

    private BenefitEnrollmentService Service(AppDbContext db, Guid userId, INotificationDispatcher? dispatcher = null) =>
        new(db, MutableTenantContext.For(TenantOf(userId)), User(userId, TenantOf(userId)),
            NullLogger<BenefitEnrollmentService>.Instance, dispatcher);

    private Guid TenantOf(Guid userId) => userId == _userB ? _tenantB : _tenantA;

    private static ITenantContext Tc(Guid tenantId) => MutableTenantContext.For(tenantId);

    private static ICurrentUser User(Guid userId, Guid tenantId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.TenantId.Returns(tenantId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("user@acme.com");
        u.Permissions.Returns(new[] { PermissionCatalog.Benefits.Manage, PermissionCatalog.Benefits.ViewOwn });
        return u;
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
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
        public static MutableTenantContext For(Guid tenantId) => new() { TenantId = tenantId };
    }
}
