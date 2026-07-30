using System.Net;
using System.Text.Json;
using FluentAssertions;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// US-ADM-012 (phase 2b, AC-1) — real HTTP proof of <c>ModuleEntitlementMiddleware</c> over the genuine
/// pipeline (TenantResolution → auth → TenantAccessGuard → Impersonation → TenantStatus → <b>ModuleEntitlement</b>
/// → controllers). The gate runs on the subdomain-resolved <c>ITenantContext</c> BEFORE the endpoint's own
/// authorization, so these arms drive it with only the dev <c>X-Tenant-Subdomain</c> header (no login needed) —
/// a non-entitled route returns 403 <c>module_not_entitled</c> before the endpoint's <c>[Authorize]</c> is reached.
///
/// <para>Each arm discriminates: the denial arm and the entitled arm share ONE tenant and differ only by path, so
/// a pass/fail can only come from the module map; the legacy-vocabulary arm proves the fail-open outage guard end
/// to end; the anonymous-careers arm proves no NRE on unauthenticated traffic.</para>
/// </summary>
[Collection("HttpApi")]
[Trait("TC", "TC-ADM-012")]
public sealed class ModuleEntitlementApiTests
{
    private readonly ApiTestFactory _factory;

    public ModuleEntitlementApiTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task NonEntitled_module_route_returns_403_module_not_entitled()
    {
        // Restricted-but-canonical plan (CoreHr + Leave; Attendance excluded).
        var subdomain = await SeedTenantAsync(new List<string> { PlanModules.CoreHr, PlanModules.Leave });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);

        var response = await client.GetAsync("/api/v1/attendance/records");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await BodyAsync(response));
        (await CodeAsync(response)).Should().Be("module_not_entitled");
    }

    [Fact]
    public async Task Entitled_module_route_is_not_gated()
    {
        // Same shape as the denial arm, only the path differs: Leave IS entitled, so the gate must let it through.
        // Unauthenticated ⇒ the endpoint's own [Authorize] then answers 401 — proving the module gate did NOT fire.
        var subdomain = await SeedTenantAsync(new List<string> { PlanModules.CoreHr, PlanModules.Leave });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);

        var response = await client.GetAsync("/api/v1/leaves/requests");

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().NotBe("module_not_entitled");
    }

    [Theory]
    [InlineData("/api/v1/recruitment/vacancies")] // Recruitment: absent from the legacy vocabulary
    [InlineData("/api/v1/tenant/employees")]       // CoreHR: absent from the legacy vocabulary
    public async Task Legacy_permission_vocabulary_tenant_is_never_locked_out(string path)
    {
        // THE outage guard, end to end: a tenant still carrying the legacy permission vocabulary (contains
        // non-canonical tokens) must be fully entitled — fail-open. A fail-CLOSED gate would 403 both of these.
        var subdomain = await SeedTenantAsync(new List<string>
        {
            "Attendance", "Audit", "Benefits", "CustomField", "Department", "Employee",
            "Leave", "Payroll", "Reports", "Roles", "Tenant", "Training",
        });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, await BodyAsync(response));
        (await CodeAsync(response)).Should().NotBe("module_not_entitled");
    }

    [Fact]
    public async Task Anonymous_careers_request_is_gated_without_500()
    {
        // CareersController is [AllowAnonymous]. With PublicCareersPage not entitled, an unauthenticated request
        // must get a clean 403 module_not_entitled — never a 500 from touching a null user.
        var subdomain = await SeedTenantAsync(new List<string> { PlanModules.CoreHr });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);

        var response = await client.GetAsync("/api/v1/careers/vacancies");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await BodyAsync(response));
        (await CodeAsync(response)).Should().Be("module_not_entitled");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<string> SeedTenantAsync(List<string> enabledModules)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var subdomain = $"adm012{suffix}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            Subdomain = subdomain,
            Name = subdomain,
            Status = TenantStatus.Active,
            PlanId = "default",
            EnabledModules = enabledModules,
        });
        await db.SaveChangesAsync();
        return subdomain;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
        }
        catch (JsonException)
        {
            return null; // non-JSON body (e.g. a 404 HTML page) — certainly not module_not_entitled
        }
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => $"Response body: {await response.Content.ReadAsStringAsync()}";
}
