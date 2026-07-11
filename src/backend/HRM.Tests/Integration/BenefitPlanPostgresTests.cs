// ============================================================================
// US-TRN-002 — benefit-plan create/Draft + audit, validation, the Draft/Active/Inactive/Archived state machine,
// tenant-default-currency resolution, before/after update audit, and tenant isolation for BenefitPlanService.
//
// HARNESS = real Postgres via Testcontainers (NOT InMemory). Persistence, the audit-row assertions, and the
// EF global-query-filter isolation are exercised against the real provider. Schema is built with
// EnsureCreatedAsync() (mirrors the other *PostgresTests — MigrateAsync is avoided because an unrelated
// uncommitted model change would trip PendingModelChangesWarning). RLS stays dormant (flag off) — isolation
// here is the EF global query filter.
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

namespace HRM.Tests.Integration;

[Trait("TC", "TC-TRN-002")]
public sealed class BenefitPlanPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();   // default currency USD
    private readonly Guid _tenantB = Guid.NewGuid();   // isolation tenant
    private readonly Guid _tenantG = Guid.NewGuid();   // default currency GBP (currency-default test)
    private readonly Guid _userA = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA));
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme", Currency = "USD" });
        db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex", Currency = "USD" });
        db.Tenants.Add(new Tenant { Id = _tenantG, Subdomain = "britco", Name = "BritCo", Currency = "GBP" });
        db.Users.Add(new User { Id = _userA, Email = "a@t.com" });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── AC-1: create → Draft + audit row ────────────────────────────────────────

    [Fact]
    public async Task Create_PersistsDraftPlan_AndWritesAuditRow()
    {
        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA));
        var service = Service(db, _tenantA);

        var result = await service.CreatePlanAsync(new CreateBenefitPlanRequest
        {
            Name = "Gold Health",
            Type = "Health",
            CoverageDetails = "Full family cover",
            EmployerCost = 300m,
            EmployeeCost = 50m,
            Currency = "USD",
            EffectiveFrom = new DateOnly(2026, 1, 1),
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Draft");
        result.Value.Name.Should().Be("Gold Health");

        await using var verify = Db(Tc(_tenantA), User(_userA, _tenantA));
        var persisted = await verify.BenefitPlans.AsNoTracking().SingleAsync(p => p.Id == result.Value.Id);
        persisted.Status.Should().Be(BenefitPlanStatus.Draft);
        persisted.TenantId.Should().Be(_tenantA);

        // FR-7: a dropped AddAudit(...) on the create path would otherwise pass green.
        var audits = await verify.AuditLogs.AsNoTracking()
            .Where(l => l.EventType == "Benefits.PlanCreated" && l.ResourceId == result.Value.Id.ToString())
            .ToListAsync();
        audits.Should().ContainSingle();
    }

    [Fact]
    public async Task Create_EmptyName_Returns400()
    {
        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA));
        var service = Service(db, _tenantA);

        var result = await service.CreatePlanAsync(
            new CreateBenefitPlanRequest { Name = "  ", Type = "Health", EffectiveFrom = new DateOnly(2026, 1, 1) },
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Create_InvalidType_Returns400()
    {
        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA));
        var service = Service(db, _tenantA);

        var result = await service.CreatePlanAsync(
            new CreateBenefitPlanRequest { Name = "Plan", Type = "Nope", EffectiveFrom = new DateOnly(2026, 1, 1) },
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Create_EffectiveToBeforeFrom_Returns400()
    {
        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA));
        var service = Service(db, _tenantA);

        var result = await service.CreatePlanAsync(new CreateBenefitPlanRequest
        {
            Name = "Plan", Type = "Health",
            EffectiveFrom = new DateOnly(2026, 7, 10), EffectiveTo = new DateOnly(2026, 7, 1),
        }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Create_NegativeCost_Returns400()
    {
        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA));
        var service = Service(db, _tenantA);

        var result = await service.CreatePlanAsync(new CreateBenefitPlanRequest
        {
            Name = "Plan", Type = "Health", EmployerCost = -5m, EffectiveFrom = new DateOnly(2026, 1, 1),
        }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ── AC-1 / US-ADM-006: blank currency resolves to the tenant default ────────

    [Fact]
    public async Task Create_BlankCurrency_ResolvesTenantDefaultCurrency()
    {
        await using var db = Db(Tc(_tenantG), User(_userA, _tenantG));
        var service = Service(db, _tenantG);

        var result = await service.CreatePlanAsync(new CreateBenefitPlanRequest
        {
            Name = "Pension", Type = "Retirement", Currency = null, EffectiveFrom = new DateOnly(2026, 1, 1),
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // BritCo's tenant default currency is GBP (US-ADM-006 Tenant.Currency).
        result.Value!.Currency.Should().Be("GBP");
    }

    // ── AC-2/AC-3/AC-6: status state machine ────────────────────────────────────

    [Fact]
    public async Task StatusTransitions_DraftToActive_Legal_IllegalRejected409()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Draft);

        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA));
        var service = Service(db, _tenantA);

        // Draft → Active is legal (makes the plan enrollable-eligible in US-TRN-003).
        var activate = await service.ChangeStatusAsync(planId, "Active", CancellationToken.None);
        activate.IsSuccess.Should().BeTrue();
        activate.Value!.Status.Should().Be("Active");

        // Active → Draft is NOT a legal transition.
        var illegal = await service.ChangeStatusAsync(planId, "Draft", CancellationToken.None);
        illegal.IsFailure.Should().BeTrue();
        illegal.StatusCode.Should().Be(409);
        illegal.ErrorCode.Should().Be("invalid_status_transition");
    }

    [Fact]
    public async Task StatusTransitions_ArchivedIsTerminal_409()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Archived);

        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA));
        var service = Service(db, _tenantA);

        var reactivate = await service.ChangeStatusAsync(planId, "Active", CancellationToken.None);
        reactivate.IsFailure.Should().BeTrue();
        reactivate.StatusCode.Should().Be(409);
        reactivate.ErrorCode.Should().Be("invalid_status_transition");
    }

    // ── AC-4: update persists cost/coverage/window + writes before/after audit ──

    [Fact]
    public async Task Update_PersistsChanges_AndWritesBeforeAfterAudit()
    {
        var planId = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active);

        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA));
        var service = Service(db, _tenantA);

        var update = await service.UpdatePlanAsync(planId, new UpdateBenefitPlanRequest
        {
            Name = "Gold Health v2",
            Type = "Health",
            CoverageDetails = "Now includes dental",
            EmployerCost = 400m,
            EmployeeCost = 75m,
            Currency = "USD",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveTo = new DateOnly(2026, 12, 31),
            EnrollmentOpensAt = new DateOnly(2026, 1, 1),
            EnrollmentClosesAt = new DateOnly(2026, 1, 31),
        }, CancellationToken.None);
        update.IsSuccess.Should().BeTrue();

        await using var verify = Db(Tc(_tenantA), User(_userA, _tenantA));
        var persisted = await verify.BenefitPlans.AsNoTracking().SingleAsync(p => p.Id == planId);
        persisted.EmployerCost.Should().Be(400m);
        persisted.EmployeeCost.Should().Be(75m);
        persisted.CoverageDetails.Should().Be("Now includes dental");
        persisted.EffectiveTo.Should().Be(new DateOnly(2026, 12, 31));
        persisted.EnrollmentClosesAt.Should().Be(new DateOnly(2026, 1, 31));

        // AC-4/FR-7: the update audit carries before/after state.
        var audit = await verify.AuditLogs.AsNoTracking()
            .SingleAsync(l => l.EventType == "Benefits.PlanUpdated" && l.ResourceId == planId.ToString());
        audit.Detail.Should().Contain("Before:");
        audit.Detail.Should().Contain("After:");
        audit.Detail.Should().Contain("employerCost=400");
    }

    // ── AC-5/AC-8: tenant isolation — plans do not leak across tenants ──────────

    [Fact]
    public async Task TenantIsolation_PlansDoNotLeak()
    {
        var planB = await SeedPlanAsync(_tenantB, BenefitPlanStatus.Active);
        var planA = await SeedPlanAsync(_tenantA, BenefitPlanStatus.Active);

        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA));
        var service = Service(db, _tenantA);

        var list = await service.GetPlansAsync(CancellationToken.None);
        list.Value!.Select(p => p.Id).Should().Contain(planA).And.NotContain(planB);

        // Tenant A cannot fetch Tenant B's plan by id (query filter → 404).
        var cross = await service.GetPlanByIdAsync(planB, CancellationToken.None);
        cross.IsFailure.Should().BeTrue();
        cross.StatusCode.Should().Be(404);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedPlanAsync(Guid tenantId, BenefitPlanStatus status)
    {
        var id = BaseEntity.NewUuidV7();
        await using var db = Db(Tc(tenantId), User(_userA, tenantId));
        db.BenefitPlans.Add(new BenefitPlan
        {
            Id = id, TenantId = tenantId, Name = "Plan " + status, Type = BenefitType.Health,
            Currency = "USD", EffectiveFrom = new DateOnly(2026, 1, 1), Status = status,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private AppDbContext Db(ITenantContext tc, ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .EnableDetailedErrors()
            .UseNpgsql(_postgres.GetConnectionString() + ";Include Error Detail=true", n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(user))
            .Options, tc);

    private BenefitPlanService Service(AppDbContext db, Guid tenantId) =>
        new(db, MutableTenantContext.For(tenantId), User(_userA, tenantId), NullLogger<BenefitPlanService>.Instance);

    private static ITenantContext Tc(Guid tenantId) => MutableTenantContext.For(tenantId);

    private static ICurrentUser User(Guid userId, Guid tenantId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.TenantId.Returns(tenantId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("user@acme.com");
        u.Permissions.Returns(new[] { PermissionCatalog.Benefits.Manage });
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
