// ============================================================================
// DF-rls-http-surface — real HTTP requests with RLS ENFORCED.
//
// These are the flows that broke under RLS and were fixed by CrossTenantScope (#472). Until now they were
// proven only at the DbContext level: the RLS suites drive raw AppDbContexts, and every HTTP test ran with
// RLS off. So the fix was verified, but never through a request — which is the only thing a user performs.
//
// Each arm below fails if the corresponding `using CrossTenantScope.Enter()` is removed from production,
// because under RLS the underlying query silently returns zero rows and the endpoint degrades to "you are not
// a member of anything".
// ============================================================================

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRM.Tests.Integration.Http;

[Trait("TC", "TC-PLT-002-RLS-HTTP")]
public sealed class RlsOnAuthFlowsApiTests : IAsyncLifetime
{
    private readonly RlsOnApiTestFactory _factory = new();

    public Task InitializeAsync() => _factory.InitializeAsync();
    public async Task DisposeAsync() => await ((IAsyncLifetime)_factory).DisposeAsync();

    /// <summary>The seeded platform tenant + admin that DbInitializer creates in Development.</summary>
    private const string PlatformSubdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    // ── The baseline: does the app even work under enforcement? ─────────

    [Fact]
    public async Task Login_succeeds_with_RLS_enforced()
    {
        // If this fails, nothing else in this file means anything — and it also proves the harness really is
        // running as the NOBYPASSRLS role rather than quietly falling back to a superuser connection.
        var client = await _factory.CreateAuthedClientAsync(PlatformSubdomain, AdminEmail, AdminPassword);

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a plain authenticated read must work with RLS on, or the flip breaks everything");
    }

    [Fact]
    public async Task The_app_is_really_running_on_the_NON_bypass_role()
    {
        // Guards against the worst outcome for this harness: RLS not actually being enforced, which would make
        // every arm in this file pass while proving nothing. It caught exactly that on first run — reporting
        // ZERO forced tables — though the cause was this arm, not the app: WebApplicationFactory boots LAZILY,
        // so without creating a client the host never starts and the startup reconciler never runs.
        _ = _factory.CreateClient();

        await using var db = _factory.CreatePrivilegedDbContext();

        var forced = await db.Database
            .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_class "
                + "WHERE relnamespace = 'public'::regnamespace AND relforcerowsecurity")
            .SingleAsync();

        forced.Should().BeGreaterThan(0,
            "the startup reconciler must have ENABLEd + FORCEd row-level security on the tenant tables");
    }

    // ── The flows CrossTenantScope exists for ──────────────────────────

    [Fact]
    public async Task My_tenants_lists_EVERY_workspace_under_RLS()
    {
        // AuthService.GetMyTenantsAsync. Without the scope this returns only the CURRENT workspace — and the
        // truncated list is then cached, so one request poisons the switcher for every later one.
        //
        // The admin is a member of ONE tenant out of the box, so simply asserting "an array came back" would
        // pass with or without the fix. A SECOND membership is seeded on the privileged connection first, so
        // the assertion genuinely requires the cross-tenant read to work.
        // Boot the host first: WebApplicationFactory is lazy, so until a client exists DbInitializer has not
        // run and the seeded admin does not exist yet.
        _ = _factory.CreateClient();

        Guid secondTenantId;
        await using (var db = _factory.CreatePrivilegedDbContext())
        {
            var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Email == AdminEmail);

            secondTenantId = BaseEntity.NewUuidV7();
            db.Tenants.Add(new Tenant
            {
                Id = secondTenantId, Subdomain = "rls-second", Name = "Second Workspace",
                Status = TenantStatus.Active, DefaultCountryCode = "LK", FiscalYearStartMonth = 1,
            });
            db.UserTenants.Add(new UserTenant
            {
                Id = BaseEntity.NewUuidV7(), UserId = admin.Id, TenantId = secondTenantId,
                Status = UserTenantStatus.Active,
            });
            await db.SaveChangesAsync();
        }

        var client = await _factory.CreateAuthedClientAsync(PlatformSubdomain, AdminEmail, AdminPassword);

        var response = await client.GetAsync("/api/v1/auth/my-tenants");
        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        raw.Should().Contain(secondTenantId.ToString(),
            "the switcher must list the OTHER workspace too. Scoped to the ambient tenant — which is what RLS "
            + "does without CrossTenantScope — this collapses to the current workspace only, and that "
            + "truncated list is then cached for every later request");
    }

    [Fact]
    public async Task A_password_change_revokes_sessions_and_does_not_500_under_RLS()
    {
        // AuthService.ChangeUserPasswordAsync revokes refresh tokens ACROSS ALL TENANTS. Under RLS, without
        // the scope, that UPDATE matches zero rows: the caller still gets a 200 while sessions in the user's
        // other workspaces stay alive on the old credential. The 200 is why nothing noticed.
        _ = _factory.CreateClient();

        // A live session in ANOTHER tenant. This is the thing the scope protects: `users` carries no
        // tenant_id, so the password update itself succeeds with or without it — only the cross-tenant
        // REVOCATION fails, silently, behind a 200. Asserting "the password changed" is therefore not enough;
        // an earlier version of this arm did exactly that and survived the mutation.
        Guid foreignTokenId;
        await using (var db = _factory.CreatePrivilegedDbContext())
        {
            var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Email == AdminEmail);

            var otherTenantId = BaseEntity.NewUuidV7();
            db.Tenants.Add(new Tenant
            {
                Id = otherTenantId, Subdomain = "rls-session", Name = "Session Co",
                Status = TenantStatus.Active, DefaultCountryCode = "LK", FiscalYearStartMonth = 1,
            });

            foreignTokenId = BaseEntity.NewUuidV7();
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = foreignTokenId,
                TenantId = otherTenantId,
                UserId = admin.Id,
                TokenHash = $"foreign-{Guid.NewGuid():N}",
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            });
            await db.SaveChangesAsync();
        }

        var client = await _factory.CreateAuthedClientAsync(PlatformSubdomain, AdminEmail, AdminPassword);

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password", new
        {
            currentPassword = AdminPassword,
            newPassword = "Rotated@2026!",
            confirmPassword = "Rotated@2026!",
        });

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);

        // The rotation must be real: the old credential must stop working.
        var reLogin = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = AdminEmail, password = AdminPassword }),
        };
        reLogin.Headers.Add("X-Tenant-Subdomain", PlatformSubdomain);

        var oldPasswordResult = await _factory.CreateClient().SendAsync(reLogin);
        oldPasswordResult.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the password change must have actually persisted under RLS");

        // THE assertion. Without CrossTenantScope the revoking UPDATE matches zero rows under RLS and this
        // session survives on the OLD credential, in a workspace the user is not currently looking at.
        await using (var verify = _factory.CreatePrivilegedDbContext())
        {
            var foreign = await verify.RefreshTokens.IgnoreQueryFilters()
                .FirstAsync(t => t.Id == foreignTokenId);

            foreign.RevokedAt.Should().NotBeNull(
                "a password change must revoke the user's sessions in EVERY tenant, not just the one they "
                + "happened to be signed into");
        }
    }

    // ── Tenant isolation, through the real pipeline ────────────────────

    [Fact]
    public async Task One_tenants_data_is_NOT_visible_from_another_tenants_host_under_RLS()
    {
        // Critical Rule #1, asserted where it actually matters: a real HTTP request, real middleware, real
        // policies. Seeded on the privileged connection so the arrangement itself is not subject to RLS.
        Guid otherTenantId;
        await using (var db = _factory.CreatePrivilegedDbContext())
        {
            otherTenantId = BaseEntity.NewUuidV7();
            db.Tenants.Add(new Tenant
            {
                Id = otherTenantId, Subdomain = "rls-other", Name = "Other Co",
                Status = TenantStatus.Active, DefaultCountryCode = "LK", FiscalYearStartMonth = 1,
            });
            db.Departments.Add(new Department
            {
                Id = BaseEntity.NewUuidV7(), TenantId = otherTenantId,
                Name = "Other-Only Department", Code = "OTH", IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var client = await _factory.CreateAuthedClientAsync(PlatformSubdomain, AdminEmail, AdminPassword);

        var response = await client.GetAsync("/api/v1/tenant/departments");
        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        raw.Should().NotContain("Other-Only Department",
            "a department belonging to another tenant must never appear in this tenant's response");
        raw.Should().NotContain(otherTenantId.ToString(),
            "not even the other tenant's id may leak into the payload");
    }

    /// <summary>
    /// TC-CHR-ISO-049 (US-CHR-013 NFR-1, Critical Rule #1) — FTE / work-arrangement edits in one tenant can
    /// never read or mutate another tenant's employee.
    ///
    /// <para>The test case was authored 2026-07-15 and explicitly targets REAL Postgres rather than InMemory,
    /// but had no automation binding — the single genuine coverage hole across the three "not tested" stories.
    /// It is implemented here rather than as a plain integration test because this harness gives it what the
    /// TC actually asks for: query filters AND RLS, exercised through the real HTTP pipeline.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-ISO-049")]
    public async Task Another_tenants_employee_is_invisible_and_unmutable_under_RLS_TC_CHR_ISO_049()
    {
        _ = _factory.CreateClient();

        // Tenant B's employee — the victim. Seeded privileged so the arrangement itself is not RLS-scoped.
        Guid victimId;
        const decimal VictimFte = 0.8m;
        await using (var db = _factory.CreatePrivilegedDbContext())
        {
            var tenantB = BaseEntity.NewUuidV7();
            var deptB = BaseEntity.NewUuidV7();
            var jobB = BaseEntity.NewUuidV7();

            db.Tenants.Add(new Tenant
            {
                Id = tenantB, Subdomain = "rls-victim", Name = "Victim Co",
                Status = TenantStatus.Active, DefaultCountryCode = "LK", FiscalYearStartMonth = 1,
            });
            db.Departments.Add(new Department
            {
                Id = deptB, TenantId = tenantB, Name = "Ops", Code = "VOPS", IsActive = true,
            });
            db.JobTitles.Add(new JobTitle
            {
                Id = jobB, TenantId = tenantB, TitleName = "Engineer", IsActive = true,
            });

            victimId = BaseEntity.NewUuidV7();
            db.Employees.Add(new Employee
            {
                Id = victimId, TenantId = tenantB, EmployeeNo = "VICTIM-1",
                FirstName = "V", LastName = "Ictim", Email = "victim@other.test",
                DateOfJoining = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DepartmentId = deptB, JobTitleId = jobB, EmploymentType = EmploymentType.FullTime,
                Status = EmployeeStatus.Active, IsActive = true, Fte = VictimFte,
            });
            await db.SaveChangesAsync();
        }

        var tenantA = await _factory.CreateAuthedClientAsync(PlatformSubdomain, AdminEmail, AdminPassword);

        // Step 1 — read another tenant's employee by id.
        var read = await tenantA.GetAsync($"/api/v1/tenant/employees/{victimId}");

        // NOT-FOUND specifically, not merely "not OK". A 403 would also satisfy `NotBe(OK)` while proving only
        // that the caller lacks a permission — nothing about tenant isolation. 404 is the isolation signal:
        // the row is invisible, so the endpoint cannot even tell the caller it exists.
        read.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "tenant B's employee must be INVISIBLE to tenant A — a 403 here would mean the arm is measuring "
            + "authorization, not isolation");
        (await read.Content.ReadAsStringAsync()).Should().NotContain("victim@other.test",
            "the victim's details must never appear in tenant A's response");

        // Step 2 — attempt to MUTATE it.
        var patch = await tenantA.PatchAsJsonAsync(
            $"/api/v1/tenant/employees/{victimId}/profile", new { fte = 1.0m });
        patch.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "tenant A must not be able to WRITE tenant B's employee");

        // Step 3 — the victim is untouched.
        await using (var verify = _factory.CreatePrivilegedDbContext())
        {
            var victim = await verify.Employees.IgnoreQueryFilters().FirstAsync(e => e.Id == victimId);
            victim.Fte.Should().Be(VictimFte,
                "a cross-tenant PATCH must leave the target employee's FTE exactly as it was");
        }
    }

    [Fact]
    public async Task System_endpoints_are_refused_from_an_ORDINARY_tenant_host_under_RLS()
    {
        // The SystemEndpointHostGuard, end to end. On a non-platform tenant host these must be refused rather
        // than routed to the tenant role, where their cross-tenant queries would return zero rows and the
        // console would half-work.
        await using (var db = _factory.CreatePrivilegedDbContext())
        {
            if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Subdomain == "rls-plain"))
            {
                db.Tenants.Add(new Tenant
                {
                    Id = BaseEntity.NewUuidV7(), Subdomain = "rls-plain", Name = "Plain Co",
                    Status = TenantStatus.Active, DefaultCountryCode = "LK", FiscalYearStartMonth = 1,
                });
                await db.SaveChangesAsync();
            }
        }

        var client = _factory.CreateClientFor("rls-plain");

        var response = await client.GetAsync("/api/v1/system/tenants");

        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized],
            "the platform namespace must not be reachable from an ordinary tenant host");
    }
}
