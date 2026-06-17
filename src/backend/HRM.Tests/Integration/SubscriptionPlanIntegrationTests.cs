// ============================================================================
// US-ADM-009: System Admin manages subscription plans — integration tests.
//
// Exercises SubscriptionPlanService over a real AppDbContext (InMemory) in the SYSTEM/admin context (no resolved
// tenant — the service reads cross-tenant data with IgnoreQueryFilters and plan/override tables have no tenant
// query filter), covering:
//   - AC-2/FR-2/FR-6: create a plan with full limits ⇒ all fields persisted; an unknown module is rejected.
//   - FR-3:           a duplicate code is rejected; (code-format is validated by the FluentValidation validator).
//   - AC-3/FR-3/BR-5: update edits all fields + writes a before/after audit; the code stays immutable.
//   - AC-4:           archive sets IsActive=false and excludes the plan from the provisioning active-plans list.
//   - FR-7/BR-5:      delete is rejected when a tenant references the plan; an unreferenced plan can be deleted.
//   - AC-1/FR-5:      the list shows the correct ACTIVE tenant count per plan.
//   - AC-5/FR-4:      a per-tenant override upsert/list/remove round-trips and the pure resolver picks it up.
//
// PROVIDER: InMemory — same rationale as the sibling integration tests (the verify gate runs `dotnet test` with
// no PostgreSQL / Docker). Real-PG behaviour is validated by the `migrations` CI job.
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

namespace HRM.Tests.Integration;

public sealed class SubscriptionPlanIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _actorUserId = Guid.NewGuid();

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

    private AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new SystemTenantContext());
    }

    private SubscriptionPlanService Service()
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(_actorUserId);
        user.IsAuthenticated.Returns(true);
        user.Email.Returns("sysadmin@platform.test");
        return new SubscriptionPlanService(Db(), user, NullLogger<SubscriptionPlanService>.Instance);
    }

    private static PlanEditableFields FullFields(
        string name = "Growth",
        int? maxEmployees = 100,
        IReadOnlyList<string>? modules = null) => new(
        Name: name,
        Description: "Growth tier",
        PriceMonthly: 99m,
        PriceYearly: 990m,
        Currency: "usd",
        TrialDays: 14,
        IsPublic: true,
        MaxEmployees: maxEmployees,
        MaxStorageGb: 100,
        MaxApiCallsPerMonth: 1_000_000,
        MaxEmailSendsPerMonth: 50_000,
        MaxCustomRoles: 20,
        MaxCustomFieldsPerEntity: 40,
        MaxWorkflows: 15,
        AuditLogRetentionDays: 365,
        SlaTier: "business",
        EnabledModules: modules ?? new[] { PlanModules.Leave, PlanModules.Attendance, PlanModules.Payroll },
        FeatureFlags: new PlanFeatureFlagsDto(Sso: true, CustomDomain: true, WhiteLabel: false, Scim: true, Sandbox: false));

    private async Task SeedTenantOnPlanAsync(string planCode, TenantStatus status = TenantStatus.Active, bool isDeleted = false)
    {
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            Subdomain = $"t-{Guid.NewGuid():N}".Substring(0, 12),
            Name = "Tenant",
            Status = status,
            PlanId = planCode,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // ── AC-2 / FR-2 / FR-6: create with full limits ───────────────────────────

    [Fact]
    public async Task Create_FullLimits_PersistsAllFieldsAndAudits()
    {
        var result = await Service().CreateAsync("growth", FullFields());

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Code.Should().Be("growth");

        using var db = Db();
        var plan = await db.SubscriptionPlans.SingleAsync(p => p.Code == "growth");
        plan.Name.Should().Be("Growth");
        plan.Description.Should().Be("Growth tier");
        plan.PriceMonthly.Should().Be(99m);
        plan.PriceYearly.Should().Be(990m);
        plan.Currency.Should().Be("USD"); // normalized to upper
        plan.TrialDays.Should().Be(14);
        plan.IsPublic.Should().BeTrue();
        plan.MaxEmployees.Should().Be(100);
        plan.MaxStorageGb.Should().Be(100);
        plan.MaxApiCallsPerMonth.Should().Be(1_000_000);
        plan.MaxEmailSendsPerMonth.Should().Be(50_000);
        plan.MaxCustomRoles.Should().Be(20);
        plan.MaxCustomFieldsPerEntity.Should().Be(40);
        plan.MaxWorkflows.Should().Be(15);
        plan.AuditLogRetentionDays.Should().Be(365);
        plan.SlaTier.Should().Be("business");
        // CoreHR is always added (FR-6) + kept in canonical order.
        plan.EnabledModules.Should().ContainInOrder(PlanModules.CoreHr, PlanModules.Leave, PlanModules.Attendance, PlanModules.Payroll);
        plan.FeatureFlags.Sso.Should().BeTrue();
        plan.FeatureFlags.Scim.Should().BeTrue();
        plan.FeatureFlags.WhiteLabel.Should().BeFalse();

        (await db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.Action == "Plan.Created" && a.ResourceId == plan.Id.ToString()))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Create_UnknownModule_IsRejected()
    {
        var fields = FullFields(modules: new[] { PlanModules.Leave, "Telepathy" });

        var result = await Service().CreateAsync("growth", fields);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("module_invalid");
        using var db = Db();
        (await db.SubscriptionPlans.AnyAsync(p => p.Code == "growth")).Should().BeFalse();
    }

    [Fact]
    public async Task Create_DuplicateCode_IsRejected()
    {
        (await Service().CreateAsync("growth", FullFields())).IsSuccess.Should().BeTrue();

        var second = await Service().CreateAsync("growth", FullFields(name: "Growth 2"));

        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("code_taken");
        using var db = Db();
        (await db.SubscriptionPlans.CountAsync(p => p.Code == "growth")).Should().Be(1);
    }

    // ── AC-3 / FR-3 / BR-5: update edits fields + audit; code immutable ───────

    [Fact]
    public async Task Update_EditsFields_AndWritesBeforeAfterAudit_CodeUnchanged()
    {
        var created = await Service().CreateAsync("growth", FullFields(maxEmployees: 100));
        var id = created.Value!.Id;

        var updated = FullFields(name: "Growth Plus", maxEmployees: 200) with { SlaTier = "enterprise" };
        var result = await Service().UpdateAsync(id, updated);

        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = Db();
        var plan = await db.SubscriptionPlans.SingleAsync(p => p.Id == id);
        plan.Code.Should().Be("growth");          // immutable (FR-3/BR-5)
        plan.Name.Should().Be("Growth Plus");
        plan.MaxEmployees.Should().Be(200);        // AC-3 limit raised
        plan.SlaTier.Should().Be("enterprise");
        plan.UpdatedAt.Should().NotBeNull();

        var audit = await db.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.Action == "Plan.Updated" && a.ResourceId == id.ToString());
        audit.Before.Should().NotBeNull();
        audit.Before!.Should().Contain("\"MaxEmployees\":100");
        audit.After.Should().NotBeNull();
        audit.After!.Should().Contain("\"MaxEmployees\":200");
    }

    // ── AC-4: archive ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Archive_SetsInactive_AndExcludesFromProvisioningActivePlans()
    {
        var created = await Service().CreateAsync("growth", FullFields());
        var id = created.Value!.Id;

        (await Service().ArchiveAsync(id)).IsSuccess.Should().BeTrue();

        using var db = Db();
        (await db.SubscriptionPlans.SingleAsync(p => p.Id == id)).IsActive.Should().BeFalse();

        // The provisioning "active plans" list (used for the Create-Tenant dropdown) must exclude it (AC-4).
        var provisioning = new TenantProvisioningService(
            Db(),
            Substitute.For<ICurrentUser>(),
            Substitute.For<ITenantWelcomeEmailService>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<TenantProvisioningService>.Instance);
        var active = await provisioning.ListActivePlansAsync();
        active.Value!.Any(p => p.Code == "growth").Should().BeFalse();
    }

    // ── FR-7 / BR-5: delete guard ─────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReferencedByTenant_IsRejected()
    {
        var created = await Service().CreateAsync("growth", FullFields());
        await SeedTenantOnPlanAsync("growth");

        var result = await Service().DeleteAsync(created.Value!.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("plan_referenced");
        using var db = Db();
        (await db.SubscriptionPlans.AnyAsync(p => p.Code == "growth")).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_ReferencedByTerminatedTenant_IsStillRejected()
    {
        var created = await Service().CreateAsync("growth", FullFields());
        await SeedTenantOnPlanAsync("growth", TenantStatus.Terminated);

        var result = await Service().DeleteAsync(created.Value!.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("plan_referenced");
    }

    [Fact]
    public async Task Delete_Unreferenced_IsAllowed()
    {
        var created = await Service().CreateAsync("growth", FullFields());

        var result = await Service().DeleteAsync(created.Value!.Id);

        result.IsSuccess.Should().BeTrue(result.Error);
        using var db = Db();
        (await db.SubscriptionPlans.AnyAsync(p => p.Code == "growth")).Should().BeFalse();
        (await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "Plan.Deleted")).Should().BeTrue();
    }

    // ── AC-1 / FR-5: active tenant count ──────────────────────────────────────

    [Fact]
    public async Task List_ShowsActiveTenantCountPerPlan()
    {
        await Service().CreateAsync("growth", FullFields(name: "Growth"));
        await Service().CreateAsync("starter", FullFields(name: "Starter"));

        await SeedTenantOnPlanAsync("growth", TenantStatus.Active);
        await SeedTenantOnPlanAsync("growth", TenantStatus.Trial);
        await SeedTenantOnPlanAsync("growth", TenantStatus.Terminated); // excluded — not active
        await SeedTenantOnPlanAsync("growth", TenantStatus.Active, isDeleted: true); // excluded — deleted
        await SeedTenantOnPlanAsync("starter", TenantStatus.Active);

        var result = await Service().ListAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        var growth = result.Value!.Single(p => p.Code == "growth");
        var starter = result.Value!.Single(p => p.Code == "starter");
        growth.ActiveTenantCount.Should().Be(2);  // Active + Trial only
        starter.ActiveTenantCount.Should().Be(1);
    }

    // ── AC-5 / FR-4: per-tenant override round-trip + resolver pickup ──────────

    [Fact]
    public async Task Override_Upsert_List_Remove_RoundTrips_AndResolverPicksItUp()
    {
        var tenantId = Guid.NewGuid();
        await SeedTenantByIdAsync(tenantId, "growth");
        await Service().CreateAsync("growth", FullFields(maxEmployees: 50));

        // Upsert: max_employees=200 for this tenant, never expires.
        var upsert = await Service().UpsertOverrideAsync(tenantId, PlanLimitKeys.MaxEmployees, value: 200, expiresAt: null);
        upsert.IsSuccess.Should().BeTrue(upsert.Error);

        // List shows it.
        var list = await Service().ListOverridesAsync(tenantId);
        list.Value!.Should().ContainSingle(o => o.LimitKey == PlanLimitKeys.MaxEmployees && o.Value == 200);

        // The pure resolver: override (200) wins over the plan cap (50).
        using (var db = Db())
        {
            var overrides = await db.PlanLimitOverrides.Where(o => o.TenantId == tenantId).ToListAsync();
            var resolved = PlanLimitResolver.Resolve(PlanLimitKeys.MaxEmployees, planValue: 50, overrides, DateTime.UtcNow);
            resolved.Value.Should().Be(200);
            resolved.Source.Should().Be(PlanLimitResolver.LimitSource.Override);
        }

        // Update the same key (upsert again) ⇒ no duplicate row.
        var upsert2 = await Service().UpsertOverrideAsync(tenantId, PlanLimitKeys.MaxEmployees, value: 300, expiresAt: null);
        upsert2.IsSuccess.Should().BeTrue();
        using (var db = Db())
        {
            (await db.PlanLimitOverrides.CountAsync(o => o.TenantId == tenantId && o.LimitKey == PlanLimitKeys.MaxEmployees))
                .Should().Be(1);
        }

        // Remove.
        var overrideId = (await Service().ListOverridesAsync(tenantId)).Value!.Single().Id;
        (await Service().RemoveOverrideAsync(overrideId)).IsSuccess.Should().BeTrue();
        (await Service().ListOverridesAsync(tenantId)).Value!.Should().BeEmpty();

        // Audit trail for all three operations.
        using (var db = Db())
        {
            (await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "Plan.OverrideCreated")).Should().BeTrue();
            (await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "Plan.OverrideUpdated")).Should().BeTrue();
            (await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "Plan.OverrideRemoved")).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Override_ForUnknownTenant_IsRejected()
    {
        var result = await Service().UpsertOverrideAsync(Guid.NewGuid(), PlanLimitKeys.MaxEmployees, 200, null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("tenant_not_found");
    }

    private async Task SeedTenantByIdAsync(Guid id, string planCode)
    {
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = id,
            Subdomain = $"t-{Guid.NewGuid():N}".Substring(0, 12),
            Name = "Tenant",
            Status = TenantStatus.Active,
            PlanId = planCode,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
