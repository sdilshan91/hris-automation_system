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
/// Real HTTP route-level arm for <b>US-PRF-005</b>'s two NEW 360 endpoints — the RELEASE action and the
/// reviewee-facing MY-RESULTS read. The service-level suite (<see cref="Feedback360IntegrationTests"/>)
/// covers the business logic exhaustively but constructs <c>Feedback360Service</c> directly, so deleting
/// either controller route or either MediatR handler would not turn any of those arms red. This class closes
/// that wiring hole over the genuine HTTP → controller → MediatR → Npgsql path:
/// <list type="number">
///   <item>an HR persona holding <c>Performance.Review.All</c> POSTing release gets <b>200</b> — proving the
///   route, the handler, and the <c>[RequirePermission]</c> filter are all wired.</item>
///   <item>a persona with NO Performance.Review permission POSTing the same route gets <b>403</b> — proving the
///   controller's <c>[RequirePermission("Performance.Review.All","Performance.Review.Team")]</c> is actually
///   applied (otherwise unverified by any test).</item>
///   <item>the reviewee GETing their own results BEFORE release gets <b>404 <c>not_released</c></b> — proving
///   that route + handler are wired and the service self-scopes over real HTTP.</item>
/// </list>
/// Isolation is by unique tenant/subdomain within the shared "HttpApi" collection (mirrors
/// <see cref="Feedback360ManagerConfigApiTests"/>).
/// </summary>
[Collection("HttpApi")]
public sealed class Feedback360ReleaseApiTests
{
    private const string PersonaPassword = "Persona@123!";

    private readonly ApiTestFactory _factory;

    public Feedback360ReleaseApiTests(ApiTestFactory factory) => _factory = factory;

    private static string ReleaseRoute(Guid cycleId, Guid employeeId)
        => $"/api/v1/tenant/performance/360/cycles/{cycleId}/employees/{employeeId}/release";

    private static string MyResultsRoute(Guid cycleId)
        => $"/api/v1/tenant/performance/360/cycles/{cycleId}/my-results";

    // ── 1. HR (Review.All) → POST release → route + handler + filter wired → 200 ──
    [Fact]
    public async Task Hr_WithReviewAll_PostRelease_ClearsFilterAndHandler_200()
    {
        var seed = await SeedAsync();
        var client = await _factory.CreateAuthedClientAsync(seed.Subdomain, seed.HrEmail, PersonaPassword);

        var response = await client.PostAsync(ReleaseRoute(seed.CycleId, seed.RevieweeEmployeeId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an HR persona holding Performance.Review.All must clear the controller gate and reach the release handler");
    }

    // ── 2. NO Performance.Review permission → controller gate stops it → 403 ──
    [Fact]
    public async Task Persona_WithoutPerformanceReviewPermission_PostRelease_IsStoppedAtControllerGate_403()
    {
        var seed = await SeedAsync();
        var client = await _factory.CreateAuthedClientAsync(seed.Subdomain, seed.NoPermEmail, PersonaPassword);

        // Even against a real, existing cycle/employee, a caller lacking Performance.Review.All/.Team never
        // clears the controller [RequirePermission] — proving the gate is actually applied to the route.
        var response = await client.PostAsync(ReleaseRoute(seed.CycleId, seed.RevieweeEmployeeId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a caller with no Performance.Review permission must be blocked by the controller gate on the release route");
    }

    // ── 3. Reviewee GET my-results BEFORE release → route + handler wired, self-scoped → 404 not_released ──
    [Fact]
    public async Task Reviewee_GetMyResults_BeforeRelease_Returns404_NotReleased()
    {
        var seed = await SeedAsync();
        var client = await _factory.CreateAuthedClientAsync(seed.Subdomain, seed.RevieweeEmail, PersonaPassword);

        var response = await client.GetAsync(MyResultsRoute(seed.CycleId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the reviewee's own results are not visible until released — 404 (never 403) so unreleased results are not disclosed");

        var code = await ReadErrorCodeAsync(response);
        code.Should().Be("not_released",
            "the reviewee-facing read must self-scope and release-gate over the genuine HTTP path");
    }

    // ── seeding ─────────────────────────────────────────────────────────────

    private sealed record Seeded(
        string Subdomain, Guid CycleId, Guid RevieweeEmployeeId,
        string HrEmail, string RevieweeEmail, string NoPermEmail);

    /// <summary>
    /// Seeds a fresh Active business tenant with one Active, 360-enabled cycle (Min360PeerReviewers = 0 so the
    /// release threshold is met with no feedback — this class asserts WIRING, not the peer-threshold logic which
    /// <see cref="Feedback360IntegrationTests"/> already covers), a reviewee EMPLOYEE linked to a reviewee USER,
    /// and three personas: an HR user (Performance.Review.All), the reviewee user (Performance.Read.Self, so it
    /// can authenticate and hit the [Authorize]-only my-results route), and a no-permission user (Employee.View.Own,
    /// which never clears the release gate). Mirrors the self-contained pattern in
    /// <see cref="Feedback360ManagerConfigApiTests.SeedAsync"/>.
    /// </summary>
    private async Task<Seeded> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenantId = Guid.NewGuid();
        var subdomain = $"p5rel{Guid.NewGuid():N}"[..14];

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Subdomain = subdomain,
            Name = "US-PRF-005 Release",
            Status = TenantStatus.Active,
            PlanId = "default",
        });

        var now = DateTime.UtcNow;
        var cycleId = Guid.NewGuid();
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = cycleId,
            TenantId = tenantId,
            Name = "US-PRF-005 360 Cycle",
            Type = CycleType.Annual,
            Status = AppraisalCycleStatus.Active,
            StartDate = now.AddDays(-1),
            EndDate = now.AddDays(89),
            RatingScaleMax = 5,
            Is360Enabled = true,
            // Min set to 1 (NOT 0: the config's HasDefaultValue(2) makes EF treat the CLR-default 0 as "unset"
            // and store 2). One seeded peer response below meets it, so the release SUCCEEDS — this class asserts
            // WIRING, not the peer-threshold logic which Feedback360IntegrationTests already covers exhaustively.
            Min360PeerReviewers = 1,
        });

        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = tenantId, Name = "Engineering", Code = $"ENG{Guid.NewGuid():N}"[..8], IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = "Engineer", IsActive = true,
        });

        // The reviewee USER + EMPLOYEE (linked by UserId — how the service resolves the caller's own employee)
        // plus a peer employee whose seeded Feedback360 row meets the min-1 peer threshold for the release arm.
        var revieweeUserId = Guid.NewGuid();
        var revieweeEmpId = Guid.NewGuid();
        var peerEmpId = Guid.NewGuid();
        db.Employees.Add(Emp(tenantId, revieweeEmpId, revieweeUserId, "RVW", "Ada", "Lovelace", deptId, jobTitleId));
        db.Employees.Add(Emp(tenantId, peerEmpId, null, "PR1", "Alan", "Turing", deptId, jobTitleId));

        db.Feedback360s.Add(new Feedback360
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CycleId = cycleId,
            RevieweeEmployeeId = revieweeEmpId,
            ReviewerEmployeeId = peerEmpId,
            Category = ReviewerCategory.Peer,
            IsAnonymous = false,
            SubmittedAt = now,
        });

        var hrEmail = await AddPersonaAsync(db, tenantId, "HR", PermissionCatalog.Performance.ReviewAll);
        var revieweeEmail = await AddPersonaAssignedToUserAsync(
            db, tenantId, revieweeUserId, "Reviewee", PermissionCatalog.Performance.ReadSelf);
        var noPermEmail = await AddPersonaAsync(db, tenantId, "NoPerm", PermissionCatalog.Employee.ViewOwn);

        await db.SaveChangesAsync();
        return new Seeded(subdomain, cycleId, revieweeEmpId, hrEmail, revieweeEmail, noPermEmail);
    }

    /// <summary>Creates a new USER (with a fresh id) + role + membership carrying exactly the given permission.</summary>
    private static Task<string> AddPersonaAsync(AppDbContext db, Guid tenantId, string label, string permission)
        => AddPersonaCoreAsync(db, tenantId, Guid.NewGuid(), label, permission);

    /// <summary>Creates a USER with a SPECIFIC id (so it links to a pre-seeded employee via that employee's UserId).</summary>
    private static Task<string> AddPersonaAssignedToUserAsync(
        AppDbContext db, Guid tenantId, Guid userId, string label, string permission)
        => AddPersonaCoreAsync(db, tenantId, userId, label, permission);

    private static async Task<string> AddPersonaCoreAsync(
        AppDbContext db, Guid tenantId, Guid userId, string label, string permission)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = $"US-PRF-005 {label}",
            IsBuiltIn = false,
            RolePermissions = [new RolePermission { Permission = permission }],
        };
        db.Roles.Add(role);

        var email = $"{label.ToLowerInvariant()}-{Guid.NewGuid():N}@prf005.test";
        var user = new User
        {
            Id = userId,
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

        await Task.CompletedTask;
        return email;
    }

    private static Employee Emp(Guid t, Guid id, Guid? userId, string no, string first, string last, Guid dept, Guid jobTitle)
        => new()
        {
            Id = id, TenantId = t, UserId = userId, EmployeeNo = no, FirstName = first, LastName = last,
            Email = $"{first.ToLowerInvariant()}-{Guid.NewGuid():N}@prf005.test", DepartmentId = dept, JobTitleId = jobTitle,
            DateOfJoining = DateTime.UtcNow.AddYears(-1), Status = EmployeeStatus.Active,
        };

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
