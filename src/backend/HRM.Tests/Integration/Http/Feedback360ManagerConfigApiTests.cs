using System.Net;
using System.Text.Json;
using FluentAssertions;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP authorization arm for <b>BUG-244 Feedback360</b>: proves the tenant-configurable, team-scoped
/// manager write-authz gate <b>composes across two layers</b> over the genuine
/// HTTP → controller → MediatR → Npgsql path:
/// <list type="number">
///   <item>the controller's coarse <c>[RequirePermission("Performance.Review.All", "Performance.Review.Team")]</c>
///   on the config-surface actions, and</item>
///   <item>the fine in-service <c>ReviewerAssignmentService.AuthorizeConfigureAsync</c> tenant-toggle +
///   direct-report check.</item>
/// </list>
/// A <b>Manager</b> persona holding <c>Performance.Review.Team</c> CLEARS the controller gate — so its outcome
/// is decided entirely by the service check: 200 for their OWN direct report, and a <b>403 with error code
/// <c>not_team_manager</c></b> for a non-report (the critical negative — the manager may not touch an arbitrary
/// employee). A persona with NO Performance.Review permission is stopped at the controller gate (403), proving
/// the coarse layer is still present. Isolation is by unique tenant/subdomain within the shared "HttpApi"
/// collection (mirrors <see cref="CyclesActiveAuthorizationApiTests"/>).
/// </summary>
[Collection("HttpApi")]
public sealed class Feedback360ManagerConfigApiTests
{
    private const string PersonaPassword = "Persona@123!";

    private readonly ApiTestFactory _factory;

    public Feedback360ManagerConfigApiTests(ApiTestFactory factory) => _factory = factory;

    private static string ConfigRoute(Guid employeeId)
        => $"/api/v1/tenant/performance/feedback-360/employees/{employeeId}/config";

    // ── 1. Manager (Review.Team) → OWN direct report → clears BOTH layers (200) ──
    [Fact]
    public async Task Manager_ConfiguringOwnDirectReport_ClearsBothLayers_200()
    {
        var seed = await SeedAsync(managerHasReviewTeam: true);
        var client = await _factory.CreateAuthedClientAsync(seed.Subdomain, seed.ManagerEmail, PersonaPassword);

        var response = await client.GetAsync(ConfigRoute(seed.ReportEmployeeId));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a manager holding Performance.Review.Team may configure their own direct report when the tenant toggle is on");
    }

    // ── 2. Manager (Review.Team) → NON-report → controller passes, SERVICE denies (403 not_team_manager) ──
    [Fact]
    public async Task Manager_ConfiguringNonReport_PassesControllerGate_ButServiceDenies_NotTeamManager()
    {
        var seed = await SeedAsync(managerHasReviewTeam: true);
        var client = await _factory.CreateAuthedClientAsync(seed.Subdomain, seed.ManagerEmail, PersonaPassword);

        var response = await client.GetAsync(ConfigRoute(seed.NonReportEmployeeId));

        // The Manager holds Review.Team, so it CLEARS the controller [RequirePermission]; a 403 here can ONLY
        // come from the in-service AuthorizeConfigureAsync direct-report check — proving the layers compose.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a manager must NOT be able to configure reviewers for an employee who does not report to them");

        var code = await ReadErrorCodeAsync(response);
        code.Should().Be("not_team_manager",
            "the denial must be the fine-grained service check, not the coarse controller permission gate");
    }

    // ── 3. NO Performance.Review permission → stopped at the controller gate (403) ──
    [Fact]
    public async Task Persona_WithoutPerformanceReviewPermission_IsStoppedAtControllerGate_403()
    {
        var seed = await SeedAsync(managerHasReviewTeam: false);
        var client = await _factory.CreateAuthedClientAsync(seed.Subdomain, seed.ManagerEmail, PersonaPassword);

        // Even targeting their OWN direct report, a caller lacking Performance.Review.All/.Team never clears the
        // controller [RequirePermission], proving the coarse layer is still present (gate broadened, not removed).
        var response = await client.GetAsync(ConfigRoute(seed.ReportEmployeeId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a caller with no Performance.Review permission must be blocked by the controller gate");
    }

    // ── seeding ─────────────────────────────────────────────────────────────

    private sealed record Seeded(string Subdomain, string ManagerEmail, Guid ReportEmployeeId, Guid NonReportEmployeeId);

    /// <summary>
    /// Seeds a fresh Active business tenant (AllowManagerReviewerConfig left at its default = true) with one
    /// Active, 360-enabled cycle, a manager USER linked to a manager EMPLOYEE (via UserId), a direct report of
    /// that manager, and a non-report (reports to a different manager). The manager's role carries either
    /// Performance.Review.Team (the manager arm) or only Employee.View.Own (the no-permission arm).
    /// </summary>
    private async Task<Seeded> SeedAsync(bool managerHasReviewTeam)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenantId = Guid.NewGuid();
        var subdomain = $"b244f{Guid.NewGuid():N}"[..14];

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Subdomain = subdomain,
            Name = "BUG-244 Feedback360",
            Status = TenantStatus.Active,
            PlanId = "default",
            // AllowManagerReviewerConfig left unset → default true.
        });

        var now = DateTime.UtcNow;
        var cycleId = Guid.NewGuid();
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = cycleId,
            TenantId = tenantId,
            Name = "BUG-244 360 Cycle",
            Type = CycleType.Annual,
            Status = AppraisalCycleStatus.Active,
            StartDate = now.AddDays(-1),
            EndDate = now.AddDays(89),
            RatingScaleMax = 5,
            Is360Enabled = true,
            Min360PeerReviewers = 2,
        });

        // The manager USER + EMPLOYEE (linked by UserId — how the service resolves the caller's employee).
        var managerUserId = Guid.NewGuid();
        var managerEmpId = Guid.NewGuid();
        var reportEmpId = Guid.NewGuid();
        var nonReportEmpId = Guid.NewGuid();
        var otherManagerEmpId = Guid.NewGuid();
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();

        // A real department + job title so the employee FKs are satisfied on Postgres.
        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = tenantId, Name = "Engineering", Code = $"ENG{Guid.NewGuid():N}"[..8], IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = "Engineer", IsActive = true,
        });

        db.Employees.AddRange(
            // Only the manager employee is linked to a portal user (UserId FK); the rest have no account.
            Emp(tenantId, managerEmpId, managerUserId, "MGR", "Grace", "Hopper", deptId, jobTitleId, reportsTo: null),
            Emp(tenantId, otherManagerEmpId, null, "MG2", "Barbara", "Liskov", deptId, jobTitleId, reportsTo: null),
            Emp(tenantId, reportEmpId, null, "RPT", "Ada", "Lovelace", deptId, jobTitleId, reportsTo: managerEmpId),
            Emp(tenantId, nonReportEmpId, null, "NRP", "Alan", "Turing", deptId, jobTitleId, reportsTo: otherManagerEmpId));

        var permissions = managerHasReviewTeam
            ? new[] { PermissionCatalog.Performance.ReviewTeam, PermissionCatalog.Performance.ViewTeam }
            : new[] { PermissionCatalog.Employee.ViewOwn };

        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = managerHasReviewTeam ? "Manager" : "Employee",
            IsBuiltIn = false,
            RolePermissions = permissions.Select(p => new RolePermission { Permission = p }).ToList(),
        };
        db.Roles.Add(role);

        var email = $"manager-{Guid.NewGuid():N}@bug244.test";
        var user = new User
        {
            Id = managerUserId,
            Email = email,
            IsActive = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(PersonaPassword, workFactor: 12),
        };
        db.Users.Add(user);

        var membership = new UserTenant
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = tenantId,
            Status = UserTenantStatus.Active,
        };
        db.UserTenants.Add(membership);
        db.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = membership.Id,
            RoleId = role.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "test",
        });

        await db.SaveChangesAsync();
        return new Seeded(subdomain, email, reportEmpId, nonReportEmpId);
    }

    private static Employee Emp(Guid t, Guid id, Guid? userId, string no, string first, string last, Guid dept, Guid jobTitle, Guid? reportsTo)
        => new()
        {
            Id = id, TenantId = t, UserId = userId, EmployeeNo = no, FirstName = first, LastName = last,
            Email = $"{first.ToLowerInvariant()}-{Guid.NewGuid():N}@bug244.test", DepartmentId = dept, JobTitleId = jobTitle,
            DateOfJoining = DateTime.UtcNow.AddYears(-1), Status = EmployeeStatus.Active, ReportsToEmployeeId = reportsTo,
        };

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
