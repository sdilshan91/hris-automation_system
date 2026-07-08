using System.Net;
using FluentAssertions;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP authorization regression for <b>BUG-244 #8</b>: the Performance module's landing read
/// <c>GET /api/v1/tenant/performance/cycles/active</c> (<c>CyclesController.GetActive</c>) used to be gated on
/// the HR-only permissions <c>Performance.SetGoal.All</c> / <c>Performance.Publish.All</c>. A plain Manager or
/// employee — who legitimately opens the Performance landing route — therefore got a <b>403</b>, breaking the
/// page. The backend broadens <b>that one action's</b> <c>[RequirePermission]</c> to also admit the module's
/// Team-level / Self-level permissions (e.g. <c>Performance.SetGoal.Team</c>, <c>Performance.Review.Team</c>,
/// <c>Performance.Read.Self</c>) while KEEPING the HR permissions. This test drives the genuine bearer-middleware
/// + <c>[RequirePermission]</c> pipeline (same model as <see cref="RoleAuthorizationApiTests"/>) over the real
/// HTTP → controller → MediatR → Npgsql path.
///
/// <para><b>Personas are built from the real seeded built-in role permission sets</b>
/// (<see cref="PermissionCatalog.DefaultPermissionsFor"/>) rather than synthetic single-permission grants, so
/// each persona mirrors a genuine role and the assertion is robust to exactly which Team/Self permission the
/// broadening admits:</para>
/// <list type="bullet">
///   <item><b>Manager</b> (Team) — holds <c>Performance.SetGoal.Team</c> / <c>Performance.Review.Team</c> /
///   <c>Performance.View.Team</c>, NO <c>.All</c>. Was 403; must now be 200. <i>The core regression.</i></item>
///   <item><b>Employee</b> (Self) — holds <c>Performance.Read.Self</c> / <c>Performance.View.Own</c>,
///   NO <c>.All</c>. Was 403; must now be 200.</item>
///   <item><b>HR Officer</b> (All) — holds <c>Performance.SetGoal.All</c>. Was 200; must STAY 200 (no
///   narrowing).</item>
///   <item><b>Recruiter</b> (no Performance permission at all) — must still be 403, proving the gate was
///   <i>broadened, not removed</i>.</item>
/// </list>
///
/// <para>Each test seeds an isolated business tenant containing exactly ONE Active appraisal cycle (so a caller
/// that clears the gate observes a genuine 200, not a 404) plus the single persona under test. Because the
/// "HttpApi" collection runs sequentially against one shared DB, isolation is by unique tenant/subdomain.</para>
///
/// <para><b>Pending note:</b> the Manager (Team) and Employee (Self) tests only pass once the controller
/// broadening lands; until then they are the failing signal that documents the fix. The HR-still-200 and the
/// no-permission-still-403 tests pass regardless.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class CyclesActiveAuthorizationApiTests
{
    private const string ActiveCycleRoute = "/api/v1/tenant/performance/cycles/active";
    private const string PersonaPassword = "Persona@123!";

    private readonly ApiTestFactory _factory;

    public CyclesActiveAuthorizationApiTests(ApiTestFactory factory) => _factory = factory;

    // ── 1. CORE REGRESSION: Team-level (Manager) persona now reaches the landing read ─────────────
    [Fact]
    public async Task Manager_TeamLevelPerformancePermission_CanReach_ActiveCycle()
    {
        var (subdomain, email) = await SeedTenantWithPersonaAsync(PermissionCatalog.BuiltInRoles.Manager);
        var client = await _factory.CreateAuthedClientAsync(subdomain, email, PersonaPassword);

        var response = await client.GetAsync(ActiveCycleRoute);

        // Pre-fix this was 403 (HR-only gate). A Manager holds Performance.SetGoal.Team / Review.Team and must
        // now clear the gate; with a seeded Active cycle present that is a genuine 200.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "a Manager holding a Team-level Performance permission must no longer be forbidden from the module landing read (BUG-244 #8)");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── 2. Self-level (Employee) persona reaches the landing read ─────────────────────────────────
    [Fact]
    public async Task Employee_SelfLevelPerformancePermission_CanReach_ActiveCycle()
    {
        var (subdomain, email) = await SeedTenantWithPersonaAsync(PermissionCatalog.BuiltInRoles.Employee);
        var client = await _factory.CreateAuthedClientAsync(subdomain, email, PersonaPassword);

        var response = await client.GetAsync(ActiveCycleRoute);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "an employee holding Performance.Read.Self must be able to open the Performance landing read (BUG-244 #8)");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── 3. HR persona (Performance.SetGoal.All) is NOT narrowed ───────────────────────────────────
    [Fact]
    public async Task HrOfficer_AllLevelPerformancePermission_StillReaches_ActiveCycle()
    {
        var (subdomain, email) = await SeedTenantWithPersonaAsync(PermissionCatalog.BuiltInRoles.HROfficer);
        var client = await _factory.CreateAuthedClientAsync(subdomain, email, PersonaPassword);

        var response = await client.GetAsync(ActiveCycleRoute);

        // The broadening must ADD Team/Self permissions, never remove the HR ones.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "HR (Performance.SetGoal.All) must retain access after the gate is broadened");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── 4. NEGATIVE: a role with NO Performance permission is still forbidden ──────────────────────
    [Fact]
    public async Task Recruiter_NoPerformancePermission_IsStillForbidden()
    {
        // Recruiter holds only Recruitment.* + Employee.View.All — no Performance.* permission whatsoever.
        var (subdomain, email) = await SeedTenantWithPersonaAsync(PermissionCatalog.BuiltInRoles.Recruiter);
        var client = await _factory.CreateAuthedClientAsync(subdomain, email, PersonaPassword);

        var response = await client.GetAsync(ActiveCycleRoute);

        // Proves the gate was BROADENED (to Performance Team/Self/All), not REMOVED: a caller with no
        // Performance permission at all must remain forbidden.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a role with no Performance permission must still be denied — the gate is broadened, not removed");
    }

    // ── seeding ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a fresh, Active business tenant containing exactly one Active appraisal cycle and a single user
    /// assigned a role whose permission set matches the given built-in role (via
    /// <see cref="PermissionCatalog.DefaultPermissionsFor"/>). Returns the tenant subdomain + the user email to
    /// log in with. Self-contained so it does not depend on DbInitializer having seeded built-in roles in any
    /// particular tenant.
    /// </summary>
    private async Task<(string Subdomain, string Email)> SeedTenantWithPersonaAsync(string builtInRoleName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenantId = Guid.NewGuid();
        var subdomain = $"b244{Guid.NewGuid():N}"[..14];

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Subdomain = subdomain,
            Name = $"BUG-244 {builtInRoleName}",
            Status = TenantStatus.Active,
            PlanId = "default",
        });

        // Exactly one ACTIVE cycle so a caller that clears the authz gate gets a genuine 200 (not a 404).
        // Phases are intentionally omitted — GetActiveAsync only requires Status == Active; the window gates
        // simply report closed, which is irrelevant to this authorization regression. TenantId is set
        // explicitly because the SaveChanges tenant interceptor does not stamp when no tenant is resolved.
        var now = DateTime.UtcNow;
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "BUG-244 Active Cycle",
            Type = CycleType.Annual,
            Status = AppraisalCycleStatus.Active,
            StartDate = now.AddDays(-1),
            EndDate = now.AddDays(89),
        });

        // A tenant-scoped role carrying the real built-in permission set (Module.Action[.Scope] strings).
        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = builtInRoleName,
            IsBuiltIn = true,
            RolePermissions = PermissionCatalog.DefaultPermissionsFor(builtInRoleName)
                .Select(p => new RolePermission { Permission = p })
                .ToList(),
        };
        db.Roles.Add(role);

        var email = $"{builtInRoleName.Replace(' ', '.').ToLowerInvariant()}-{Guid.NewGuid():N}@bug244.test";
        var user = new User
        {
            Id = Guid.NewGuid(),
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
        return (subdomain, email);
    }
}
