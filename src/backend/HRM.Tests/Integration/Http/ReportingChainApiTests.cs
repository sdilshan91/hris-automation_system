// ============================================================================
// DF-57 — full-chain HTTP-route regression arm for the reporting-chain endpoint (DF-8 / US-CHR-011).
//
// DF-8's GET /api/v1/tenant/employees/{id}/reporting-chain was covered only by InMemory service-layer arms
// that call ReportingStructureService directly — nothing drove route→controller→MediatR→handler, so removing
// the controller action or mis-registering GetReportingChainQuery would fail ZERO tests. This drives the real
// HTTP pipeline over real Postgres (ApiTestFactory / Testcontainers) as an Employee.View.All persona and
// asserts the ascending chain + tenant truncation — a wiring regression would break the 200/order assertions.
// ============================================================================

using System.Net;
using System.Text.Json;
using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

[Collection("HttpApi")]
public sealed class ReportingChainApiTests
{
    private const string PersonaPassword = "Persona@123!";
    private readonly ApiTestFactory _factory;

    public ReportingChainApiTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    [Trait("TC", "TC-CHR-011-14")]
    public async Task GetReportingChain_ReturnsAscendingChain_AndTruncatesAtCrossTenantRung()
    {
        var seed = await SeedChainWithPersonaAsync();
        var client = await _factory.CreateAuthedClientAsync(seed.Subdomain, seed.Email, PersonaPassword);

        var resp = await client.GetAsync($"/api/v1/tenant/employees/{seed.EmployeeId}/reporting-chain");
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("employeeId").GetGuid().Should().Be(seed.EmployeeId);

        var chain = data.GetProperty("chain").EnumerateArray().ToList();
        chain.Select(e => e.GetProperty("id").GetGuid())
            .Should().Equal(new[] { seed.EmployeeId, seed.ManagerId, seed.RootId },
                "the chain ascends employee → manager → root");
        chain.Select(e => e.GetProperty("id").GetGuid())
            .Should().NotContain(seed.GhostId, "the cross-tenant rung above root truncates the walk");

        // The full projection resolves through the route: name = "First Last", jobTitle from the batched lookup.
        chain[0].GetProperty("name").GetString().Should().Be("Leaf Employee");
        chain[0].GetProperty("jobTitle").GetString().Should().Be("Engineer");

        // Handler not-found path over the real route (proves the query dispatches, not just a route match).
        var notFound = await client.GetAsync($"/api/v1/tenant/employees/{Guid.NewGuid()}/reporting-chain");
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("TC", "TC-CHR-011-14")]
    public async Task GetReportingChain_Returns403_ForAPersonaWithoutEmployeeViewAll()
    {
        // A genuinely-scoped persona lacking Employee.View.All → the [RequirePermission] gate must 403 over the
        // real pipeline (authz fails before the handler, so the id is irrelevant).
        var client = await _factory.CreateClientWithPermissionsAsync("Employee.View.Own");

        var resp = await client.GetAsync($"/api/v1/tenant/employees/{Guid.NewGuid()}/reporting-chain");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<ChainSeed> SeedChainWithPersonaAsync()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var subdomain = $"df57{Guid.NewGuid():N}"[..14];
        var email = $"viewer-{Guid.NewGuid():N}@df57.test";

        var deptId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var mgrId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var ghostId = Guid.NewGuid(); // lives in another tenant → filtered out → truncation above root

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // TenantId is stamped explicitly — the SaveChanges tenant interceptor does not stamp when no tenant is
        // resolved during seeding (same as CyclesActiveAuthorizationApiTests / ApiTestFactory).
        db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = subdomain, Name = "DF-57", Status = TenantStatus.Active, PlanId = "default" });
        db.Tenants.Add(new Tenant { Id = otherTenantId, Subdomain = subdomain + "x", Name = "DF-57 other", Status = TenantStatus.Active, PlanId = "default" });

        db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = "Engineering", Code = "ENG", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = titleId, TenantId = tenantId, TitleName = "Engineer", IsActive = true });

        db.Employees.Add(NewEmp(ghostId, otherTenantId, deptId, titleId, "Ghost", "Root", reportsTo: null));
        db.Employees.Add(NewEmp(rootId, tenantId, deptId, titleId, "Root", "Boss", reportsTo: ghostId)); // → cross-tenant rung
        db.Employees.Add(NewEmp(mgrId, tenantId, deptId, titleId, "Mid", "Manager", reportsTo: rootId));
        db.Employees.Add(NewEmp(empId, tenantId, deptId, titleId, "Leaf", "Employee", reportsTo: mgrId));

        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "DF-57 Viewer",
            IsBuiltIn = false,
            RolePermissions = new[] { "Employee.View.All" }.Select(p => new RolePermission { Permission = p }).ToList(),
        };
        db.Roles.Add(role);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            IsActive = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(PersonaPassword, workFactor: 12),
        };
        db.Users.Add(user);

        var membership = new UserTenant { Id = Guid.NewGuid(), UserId = user.Id, TenantId = tenantId, Status = UserTenantStatus.Active };
        db.UserTenants.Add(membership);
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = membership.Id, RoleId = role.Id, AssignedAt = DateTime.UtcNow, AssignedBy = "test" });

        await db.SaveChangesAsync();

        return new ChainSeed(subdomain, email, empId, mgrId, rootId, ghostId);
    }

    private static Employee NewEmp(Guid id, Guid tenantId, Guid deptId, Guid titleId, string first, string last, Guid? reportsTo) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            EmployeeNo = $"EMP-{id.ToString("N")[..6]}",
            FirstName = first,
            LastName = last,
            Email = $"{first}.{id:N}@df57.test".ToLowerInvariant(),
            DateOfJoining = DateTime.UtcNow.Date.AddYears(-1),
            DepartmentId = deptId,
            JobTitleId = titleId,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            ReportsToEmployeeId = reportsTo,
            IsActive = true,
        };

    private sealed record ChainSeed(string Subdomain, string Email, Guid EmployeeId, Guid ManagerId, Guid RootId, Guid GhostId);
}
