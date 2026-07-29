// ============================================================================
// ISSUE-342 (US-ADM-012) — SubscriptionPlanService plan-edit module-propagation sweep.
//
// When a plan's MODULE list changes, every tenant on that plan (joined by string code, Tenant.PlanId ==
// SubscriptionPlan.Code — there is no FK) must have its denormalized Tenant.EnabledModules snapshot recomputed
// via the shared PlanModules.DeriveTenantModules and its resolution cache invalidated. A non-module edit
// (price/name/limit) must NOT sweep, and a tenant whose PlanId is dangling (points at a plan that was never
// seeded, e.g. the "default" DbInitializer stamps) must be left untouched (fail open).
//
// HARNESS = real Postgres via Testcontainers (NOT InMemory), deliberately: this touches (1) the jsonb
// EnabledModules column round-trip and (2) a cross-tenant IgnoreQueryFilters sweep — both of which InMemory
// masks (InMemory stores List<string> by reference and does not exercise the jsonb value-converter or the
// provider's filter translation). Schema via EnsureCreatedAsync(), RLS dormant — same shape as the sibling
// *PostgresTests. Each [Fact] uses unique plan codes / subdomains so the shared container stays collision-free.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.SubscriptionPlans.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-ADM-012")]
public sealed class SubscriptionPlanModuleSweepPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _actorUserId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── The sweep: a module-list change recomputes ONLY the tenants on that plan + invalidates their cache ──

    [Fact]
    public async Task Update_ModuleListChanged_RecomputesTenantsOnThePlan_AndInvalidatesTheirCache_OtherPlansUntouched()
    {
        var code = UniqueCode();
        var onPlanA = SubOf("a1");
        var onPlanB = SubOf("a2");
        var onOtherPlan = SubOf("other");

        // Seed the plan with {CoreHR, Leave, Payroll} and three tenants: two on the plan (carrying the OLD
        // derived snapshot) and one on a DIFFERENT plan code (the control that must never be swept).
        await using (var seed = Db())
        {
            seed.SubscriptionPlans.Add(Plan(code, new() { PlanModules.CoreHr, PlanModules.Leave, PlanModules.Payroll }));
            seed.Tenants.Add(Tenant(onPlanA, code, new() { PlanModules.CoreHr, PlanModules.Leave, PlanModules.Payroll }));
            seed.Tenants.Add(Tenant(onPlanB, code, new() { PlanModules.CoreHr, PlanModules.Leave, PlanModules.Payroll }));
            seed.Tenants.Add(Tenant(onOtherPlan, "some-other-plan", new() { PlanModules.CoreHr, PlanModules.Payroll }));
            await seed.SaveChangesAsync();
        }

        var cache = new RecordingResolutionCache();
        await using (var db = Db())
        {
            var planId = await db.SubscriptionPlans.Where(p => p.Code == code).Select(p => p.Id).SingleAsync();
            // Swap Payroll → Attendance: a genuine module-set change ⇒ the sweep must fire.
            var result = await Service(db, cache).UpdateAsync(
                planId, Fields(modules: new[] { PlanModules.Leave, PlanModules.Attendance }));
            result.IsSuccess.Should().BeTrue(result.Error);
        }

        var expected = new[] { PlanModules.CoreHr, PlanModules.Leave, PlanModules.Attendance };
        await using (var verify = Db())
        {
            // Both tenants ON the plan recomputed to the NEW derived set — asserting the exact set, not merely
            // "changed", so a sweep that copied the plan's raw list (missing the CoreHR-always rule) or wrote a
            // stale value would fail.
            (await ModulesOf(verify, onPlanA)).Should().Equal(expected);
            (await ModulesOf(verify, onPlanB)).Should().Equal(expected);

            // The tenant on a DIFFERENT plan is untouched — proves the sweep is scoped by plan code, not global.
            (await ModulesOf(verify, onOtherPlan)).Should().Equal(PlanModules.CoreHr, PlanModules.Payroll);
        }

        // Exactly the two swept tenants' subdomains were invalidated — not the control tenant's.
        cache.Invalidated.Should().BeEquivalentTo(new[] { onPlanA, onPlanB });
        cache.Invalidated.Should().NotContain(onOtherPlan);
    }

    // ── A non-module edit (price/name) must NOT sweep — no tenant churn, no cache invalidation ──

    [Fact]
    public async Task Update_NonModuleEdit_DoesNotSweep()
    {
        var code = UniqueCode();
        var sub = SubOf("static");

        await using (var seed = Db())
        {
            seed.SubscriptionPlans.Add(Plan(code, new() { PlanModules.CoreHr, PlanModules.Leave }));
            // UpdatedAt left null so any accidental touch is detectable.
            seed.Tenants.Add(Tenant(sub, code, new() { PlanModules.CoreHr, PlanModules.Leave }));
            await seed.SaveChangesAsync();
        }

        var cache = new RecordingResolutionCache();
        await using (var db = Db())
        {
            var planId = await db.SubscriptionPlans.Where(p => p.Code == code).Select(p => p.Id).SingleAsync();
            // Same module set ({Leave} normalizes back to {CoreHR, Leave}) but a different name + price.
            var result = await Service(db, cache).UpdateAsync(
                planId, Fields(name: "Renamed", priceMonthly: 999m, modules: new[] { PlanModules.Leave }));
            result.IsSuccess.Should().BeTrue(result.Error);
        }

        await using (var verify = Db())
        {
            var tenant = await verify.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Subdomain == sub);
            tenant.EnabledModules.Should().Equal(PlanModules.CoreHr, PlanModules.Leave);
            tenant.UpdatedAt.Should().BeNull("a non-module edit must not touch tenants on the plan");
        }

        cache.Invalidated.Should().BeEmpty("a non-module plan edit must not invalidate any tenant's cache");
    }

    // ── Fail-open: a tenant whose PlanId is dangling ("default", never seeded as a plan) is left untouched ──

    [Fact]
    public async Task Update_DanglingPlanId_FailsOpen_TenantLeftUntouched()
    {
        var code = UniqueCode();
        var danglingSub = SubOf("dangling");

        await using (var seed = Db())
        {
            seed.SubscriptionPlans.Add(Plan(code, new() { PlanModules.CoreHr, PlanModules.Leave }));
            // PlanId "default" has NO matching plan row (DbInitializer stamps it but never seeds it).
            seed.Tenants.Add(Tenant(danglingSub, "default", new() { PlanModules.CoreHr, PlanModules.Payroll }));
            await seed.SaveChangesAsync();
        }

        var cache = new RecordingResolutionCache();
        await using (var db = Db())
        {
            var planId = await db.SubscriptionPlans.Where(p => p.Code == code).Select(p => p.Id).SingleAsync();
            // Editing the REAL plan's modules must neither crash on nor recompute the dangling tenant.
            var result = await Service(db, cache).UpdateAsync(
                planId, Fields(modules: new[] { PlanModules.Leave, PlanModules.Attendance }));
            result.IsSuccess.Should().BeTrue(result.Error);
        }

        await using (var verify = Db())
        {
            var tenant = await verify.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Subdomain == danglingSub);
            tenant.EnabledModules.Should().Equal(PlanModules.CoreHr, PlanModules.Payroll);
            tenant.UpdatedAt.Should().BeNull("a dangling PlanId must fail open — never stripped, never recomputed");
        }

        cache.Invalidated.Should().NotContain(danglingSub);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string UniqueCode() => $"p{Guid.NewGuid():N}"[..12];
    private static string SubOf(string tag) => $"{tag}-{Guid.NewGuid():N}"[..14];

    private static async Task<List<string>> ModulesOf(AppDbContext db, string subdomain)
        => (await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Subdomain == subdomain)).EnabledModules;

    private static SubscriptionPlan Plan(string code, List<string> modules) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        Code = code,
        Name = code,
        Currency = "USD",
        IsActive = true,
        MaxEmployees = 100,
        EnabledModules = modules,
        FeatureFlags = new PlanFeatureFlags(),
        CreatedAt = DateTime.UtcNow,
    };

    private static Tenant Tenant(string subdomain, string planCode, List<string> modules) => new()
    {
        Id = Guid.NewGuid(),
        Subdomain = subdomain,
        Name = subdomain,
        Status = TenantStatus.Active,
        PlanId = planCode,
        EnabledModules = modules,
        CreatedAt = DateTime.UtcNow,
    };

    private static PlanEditableFields Fields(
        string name = "Plan",
        decimal priceMonthly = 99m,
        int? maxEmployees = 100,
        IReadOnlyList<string>? modules = null) => new(
        Name: name,
        Description: null,
        PriceMonthly: priceMonthly,
        PriceYearly: priceMonthly * 10,
        Currency: "USD",
        TrialDays: 0,
        IsPublic: true,
        MaxEmployees: maxEmployees,
        MaxStorageGb: null,
        MaxApiCallsPerMonth: null,
        MaxEmailSendsPerMonth: null,
        MaxCustomRoles: null,
        MaxCustomFieldsPerEntity: null,
        MaxWorkflows: null,
        AuditLogRetentionDays: 90,
        SlaTier: "standard",
        EnabledModules: modules ?? new[] { PlanModules.Leave },
        FeatureFlags: new PlanFeatureFlagsDto(false, false, false, false, false));

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options, new SystemTenantContext());

    private SubscriptionPlanService Service(AppDbContext db, ITenantResolutionCache cache)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(_actorUserId);
        user.IsAuthenticated.Returns(true);
        return new SubscriptionPlanService(db, user, NullLogger<SubscriptionPlanService>.Instance, cache);
    }

    /// <summary>Records the subdomains passed to the resolution-cache invalidator so the sweep's cache side is assertable.</summary>
    private sealed class RecordingResolutionCache : ITenantResolutionCache
    {
        public List<string> Invalidated { get; } = new();

        public Task InvalidateAsync(string subdomain, CancellationToken cancellationToken = default)
        {
            Invalidated.Add(subdomain);
            return Task.CompletedTask;
        }

        public Task InvalidateManyAsync(IEnumerable<string> subdomains, CancellationToken cancellationToken = default)
        {
            Invalidated.AddRange(subdomains);
            return Task.CompletedTask;
        }
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
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }
}
