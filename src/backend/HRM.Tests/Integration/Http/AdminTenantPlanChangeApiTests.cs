using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// ISSUE-341 (US-ADM-012) — real HTTP proof for <c>PUT /api/v1/system/tenants/{id}/plan</c>: the genuine
/// HTTP → <c>[RequirePermission("Plan.Manage")]</c> → controller → MediatR → service → Npgsql path.
///
/// <para>Covers: (1) the SystemAdmin happy path moves a tenant onto the target plan and returns the RECOMPUTED
/// entitlement snapshot (the target is seeded on the dangling <c>"default"</c> PlanId, so this also proves the
/// move fails open on a dangling source code); (2) an unknown/inactive plan code is rejected with 400
/// <c>plan_invalid</c>; (3) a caller holding only <c>Plan.View</c> (read-only System Support) is denied with 403,
/// proving the gate runs before the handler.</para>
/// </summary>
[Collection("HttpApi")]
[Trait("TC", "TC-ADM-012")]
public sealed class AdminTenantPlanChangeApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public AdminTenantPlanChangeApiTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task ChangePlan_AsSystemAdmin_MovesTenantAndReturnsRecomputedModules()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var planCode = $"iss341-{suffix}";
        // A RESTRICTED plan (only Attendance) so the recompute is provably a subset: CoreHR is always added, but
        // Payroll must NOT appear. A handler that echoed the full canonical set would fail this.
        var tenantId = await SeedTenantAndPlanAsync(
            planCode, planModules: new List<string> { PlanModules.Attendance }, tenantSubdomain: $"iss341t{suffix}");

        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/system/tenants/{tenantId}/plan", new { planCode });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("planCode").GetString().Should().Be(planCode);

        var modules = data.GetProperty("enabledModules").EnumerateArray().Select(e => e.GetString()).ToList();
        modules.Should().Contain(new[] { PlanModules.CoreHr, PlanModules.Attendance });
        modules.Should().NotContain(PlanModules.Payroll, "the target plan is restricted to Attendance (+ always-on CoreHR)");

        // Persisted: the tenant now points at the new plan code (moved off the dangling "default").
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
        persisted.PlanId.Should().Be(planCode);
    }

    [Fact]
    public async Task ChangePlan_UnknownPlan_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAndPlanAsync(
            $"iss341real-{suffix}", planModules: new List<string> { PlanModules.Leave }, tenantSubdomain: $"iss341u{suffix}");

        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/system/tenants/{tenantId}/plan", new { planCode = $"nope-{suffix}" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await BodyAsync(response));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("plan_invalid");
    }

    [Fact]
    public async Task ChangePlan_AsCallerWithoutPlanManage_Returns403()
    {
        // Read-only "System Support" equivalent: holds Plan.View but NOT Plan.Manage.
        var client = await _factory.CreateClientWithPermissionsAsync(PermissionCatalog.Plan.View);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/system/tenants/{Guid.NewGuid()}/plan", new { planCode = "anything" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Plan.View must not be able to change a tenant's plan — only Plan.Manage (SystemAdmin) can: "
            + await BodyAsync(response));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds an ACTIVE target plan with the given module list plus a tenant on the dangling <c>"default"</c>
    /// PlanId (so the move also exercises the fail-open dangling-source path). Returns the tenant id.
    /// </summary>
    private async Task<Guid> SeedTenantAndPlanAsync(string planCode, List<string> planModules, string tenantSubdomain)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = BaseEntity.NewUuidV7(),
            Code = planCode,
            Name = planCode,
            Currency = "USD",
            IsActive = true,
            MaxEmployees = 50,
            EnabledModules = planModules,
            FeatureFlags = new PlanFeatureFlags(),
        });

        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Subdomain = tenantSubdomain,
            Name = tenantSubdomain,
            Status = TenantStatus.Active,
            PlanId = "default", // dangling — no plan with Code == "default" is seeded
            EnabledModules = new List<string> { PlanModules.CoreHr, PlanModules.Payroll },
        });

        await db.SaveChangesAsync();
        return tenantId;
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => $"Response body: {await response.Content.ReadAsStringAsync()}";
}
