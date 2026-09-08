using System.Net;
using System.Text.Json;
using FluentAssertions;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// ENH-002 — real-HTTP regression for US-LV-012 <b>BR-2</b>: <i>"Employee-level data in reports respects
/// role-based access: HR sees all; managers see their team; employees see only their own data."</i>
///
/// <para><b>THE DEFECT.</b> BR-2's manager and self branches WERE implemented in
/// <c>LeaveReportService.ResolveScopeAsync</c> / <c>ScopedEmployeesQuery</c> — but every leave
/// report/analytics/export endpoint was gated solely on <c>Leave.Reports</c>, which only the admin-tier
/// roles (Tenant Admin / HR Manager / HR Officer / Auditor) hold. A Manager or Employee was therefore
/// rejected with a <b>403 before the scope branches could run</b>, so two thirds of BR-2 was dead code.
/// The fix adds <c>Leave.Reports.Team</c> / <c>Leave.Reports.Own</c>, seeds them onto the built-in Manager
/// and Employee roles, and broadens the gate to admit ANY of the three.</para>
///
/// <para><b>WHY THESE TESTS ASSERT ROWS, NOT STATUS CODES.</b> Broadening an authorization gate is only
/// correct if the row scope still binds afterwards. A 200 alone would pass just as happily against the
/// wrong fix — widening <c>Leave.Reports</c> onto every role — which would hand a Manager the whole
/// organisation. So each persona test reads the <c>Employee No</c> column out of the BalanceSummary
/// report and asserts the EXACT set of employees returned:</para>
/// <list type="bullet">
///   <item><b>Manager</b> (<c>Leave.Reports.Team</c>) — self + own direct report ONLY.</item>
///   <item><b>Manager</b> must NOT see the other team's manager or their report (scope still binds).</item>
///   <item><b>Employee</b> (<c>Leave.Reports.Own</c>) — exactly one row set: their own.</item>
///   <item><b>HR Manager</b> (<c>Leave.Reports</c> + <c>Reports.View.All</c>) — the full org, unchanged.</item>
///   <item><b>Recruiter</b> (no <c>Leave.Reports*</c> at all) — still 403; the gate was broadened, not removed.</item>
/// </list>
///
/// <para>Personas are built from the real seeded built-in permission sets
/// (<see cref="PermissionCatalog.DefaultPermissionsFor"/>) rather than synthetic grants, so the test also
/// proves the ROLE SEEDING half of the fix: if <c>Leave.Reports.Team</c> were added to the catalog but not
/// to the Manager role, the manager arm goes red. Mirrors <see cref="CyclesActiveAuthorizationApiTests"/>.</para>
///
/// <para>Each test seeds its own tenant (the "HttpApi" collection shares one database and runs
/// sequentially, so isolation is by tenant/subdomain).</para>
/// </summary>
/// <remarks>
/// Tagged with the story + business rule rather than a <c>TC</c> id: the Leave module's QA specs use a
/// FLAT <c>TC-LV-NNN</c> sequence (highest allocated: TC-LV-269) and no spec exists yet for these six
/// arms. Inventing an id here would produce a reference that looks resolvable and is not — worse than no
/// id. QA to allocate TC-LV-270..275 in <c>docs/QA/leave-management/</c> and add the TC traits then.
/// </remarks>
[Collection("HttpApi")]
public sealed class LeaveReportScopeAuthorizationApiTests
{
    private const int ReportYear = 2026;
    private const string BalanceSummaryRoute =
        "/api/v1/leaves/reports/BalanceSummary?year=2026&pageSize=100";
    private const string PersonaPassword = "Persona@123!";

    // Employee numbers seeded into every test tenant. Two teams plus an unmanaged HR employee, so
    // "team scope" is a strictly smaller set than "all" and a manager has somebody to NOT see.
    private const string MgrOwnNo = "E-MGR1";   // the Manager persona's own employee record
    private const string MgrReportNo = "E-RPT1"; // reports to MgrOwn — inside the manager's team
    private const string OtherMgrNo = "E-MGR2";  // a DIFFERENT team's manager
    private const string OtherEmpNo = "E-RPT2";  // reports to OtherMgr — the Employee persona
    private const string HrEmpNo = "E-HR1";      // an HR employee reporting to nobody

    private readonly ApiTestFactory _factory;

    public LeaveReportScopeAuthorizationApiTests(ApiTestFactory factory) => _factory = factory;

    // ── 1. CORE: Manager reaches the endpoint AND gets TEAM-scoped rows ───────────────────────────
    [Fact]
    [Trait("US", "US-LV-012")]
    [Trait("BR", "BR-2")]
    public async Task Manager_WithLeaveReportsTeam_ReachesReport_AndSeesOnlyTheirTeam()
    {
        var org = await SeedOrgAsync();
        var client = await _factory.CreateAuthedClientAsync(org.Subdomain, org.ManagerEmail, PersonaPassword);

        var response = await client.GetAsync(BalanceSummaryRoute);

        // Pre-fix this was 403: the built-in Manager role holds no Leave.Reports permission at all.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "a Manager holding Leave.Reports.Team must reach the leave report handler — BR-2 gives managers "
            + "the team slice, and gating solely on Leave.Reports made that branch unreachable (ENH-002)");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));

        var (scope, employeeNos) = await ReadScopeAndEmployeeNosAsync(response);

        scope.Should().Be("Manager", "the BR-2 row scope resolved for a Leave.Reports.Team holder who "
            + "actually manages someone must be Manager, not Employee");
        employeeNos.Should().BeEquivalentTo(new[] { MgrOwnNo, MgrReportNo },
            "BR-2 team scope is the manager's own record plus their direct reports — nothing else");
    }

    // ── 2. The scope STILL BINDS after the gate opens: no cross-team leakage ──────────────────────
    [Fact]
    [Trait("US", "US-LV-012")]
    [Trait("BR", "BR-2")]
    public async Task Manager_DoesNotSee_AnotherTeamsRows()
    {
        var org = await SeedOrgAsync();
        var client = await _factory.CreateAuthedClientAsync(org.Subdomain, org.ManagerEmail, PersonaPassword);

        var response = await client.GetAsync(BalanceSummaryRoute);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));

        var (_, employeeNos) = await ReadScopeAndEmployeeNosAsync(response);

        // This is the assertion that separates "broadened the gate" from "widened the permission". All four
        // of these employees have seeded ledger activity, so they WOULD appear under an unscoped read.
        employeeNos.Should().NotContain(OtherMgrNo, "another team's manager is outside BR-2 team scope");
        employeeNos.Should().NotContain(OtherEmpNo, "another team's employee is outside BR-2 team scope");
        employeeNos.Should().NotContain(HrEmpNo, "an employee the caller does not manage is outside team scope");
    }

    // ── 3. Employee reaches the endpoint AND gets SELF-scoped rows only ───────────────────────────
    [Fact]
    [Trait("US", "US-LV-012")]
    [Trait("BR", "BR-2")]
    public async Task Employee_WithLeaveReportsOwn_ReachesReport_AndSeesOnlyThemselves()
    {
        var org = await SeedOrgAsync();
        var client = await _factory.CreateAuthedClientAsync(org.Subdomain, org.EmployeeEmail, PersonaPassword);

        var response = await client.GetAsync(BalanceSummaryRoute);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "an employee holding Leave.Reports.Own must reach the leave report handler — BR-2 gives them "
            + "their own data (ENH-002)");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));

        var (scope, employeeNos) = await ReadScopeAndEmployeeNosAsync(response);

        scope.Should().Be("Employee", "an employee who manages nobody resolves to self scope");
        employeeNos.Should().BeEquivalentTo(new[] { OtherEmpNo },
            "BR-2 self scope is exactly one employee: the caller. Holding Leave.Reports.Own must NOT widen "
            + "the row scope — the permission opens the gate, the scope decides the rows");
    }

    // ── 4. Admin tier is NOT narrowed — Leave.Reports still means the whole org ───────────────────
    [Fact]
    [Trait("US", "US-LV-012")]
    [Trait("BR", "BR-2")]
    public async Task HrManager_WithLeaveReports_StillSeesTheFullOrganisation()
    {
        var org = await SeedOrgAsync();
        var client = await _factory.CreateAuthedClientAsync(org.Subdomain, org.HrEmail, PersonaPassword);

        var response = await client.GetAsync(BalanceSummaryRoute);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));

        var (scope, employeeNos) = await ReadScopeAndEmployeeNosAsync(response);

        scope.Should().Be("All", "HR Manager holds Reports.View.All → the org-wide BR-2 bucket");
        employeeNos.Should().BeEquivalentTo(
            new[] { MgrOwnNo, MgrReportNo, OtherMgrNo, OtherEmpNo, HrEmpNo },
            "broadening the gate must ADD the Team/Own grants, never narrow what the admin tier sees");
    }

    // ── 5. NEGATIVE: no Leave.Reports* permission at all is still 403 ─────────────────────────────
    [Fact]
    [Trait("US", "US-LV-012")]
    [Trait("BR", "BR-2")]
    public async Task Recruiter_WithNoLeaveReportPermission_IsStillForbidden()
    {
        // Recruiter holds Recruitment.* + Employee.View.All — no Leave permission of any kind.
        PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Recruiter)
            .Should().NotContain(p => p.StartsWith("Leave.Reports", StringComparison.Ordinal),
                "this arm only proves anything while the Recruiter persona genuinely lacks the permission");

        var org = await SeedOrgAsync();
        var client = await _factory.CreateAuthedClientAsync(org.Subdomain, org.RecruiterEmail, PersonaPassword);

        var response = await client.GetAsync(BalanceSummaryRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the gate was broadened to three named permissions, not removed — a caller holding none of "
            + "Leave.Reports / Leave.Reports.Team / Leave.Reports.Own must still be denied");
    }

    // ── 6. Leave.Reports.Team ALONE confers team scope, not just gate access ─────────────────────
    [Fact]
    [Trait("US", "US-LV-012")]
    [Trait("BR", "BR-2")]
    public async Task LeaveReportsTeam_Alone_ConfersTeamScope_NotSelfScope()
    {
        // WHY THIS ARM EXISTS. The built-in Manager role ALSO holds the cross-module Reports.View.Team,
        // which the BR-2 scope resolver has keyed on since DEC-1 — so arms 1 and 2 would stay green even if
        // Leave.Reports.Team only opened the gate and contributed nothing to the scope. This persona holds a
        // CUSTOM role granting exactly ONE permission, Leave.Reports.Team, and nothing else. If the resolver
        // did not honour it, the caller would clear the gate and then be handed SELF-scoped rows — a
        // permission whose name lies about the scope it grants. That is the failure this arm catches.
        var org = await SeedOrgAsync();
        var client = await _factory.CreateAuthedClientAsync(
            org.Subdomain, org.MinimalTeamGrantEmail, PersonaPassword);

        var response = await client.GetAsync(BalanceSummaryRoute);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));

        var (scope, employeeNos) = await ReadScopeAndEmployeeNosAsync(response);

        scope.Should().Be("Manager",
            "Leave.Reports.Team on its own must resolve the BR-2 TEAM bucket — without Reports.View.Team to "
            + "fall back on, a resolver that ignored it would silently downgrade this caller to self scope");
        employeeNos.Should().BeEquivalentTo(new[] { OtherMgrNo, OtherEmpNo },
            "the persona is the second team's manager: their own record plus their one direct report");
    }

    // ── response reading ──────────────────────────────────────────────────────────────────────────

    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => $"Response body: {await response.Content.ReadAsStringAsync()}";

    /// <summary>
    /// Reads the BR-2 scope echo plus the DISTINCT set of employee numbers present in the report rows.
    /// The "Employee No" column index is resolved from the returned <c>columns</c> array rather than
    /// hard-coded, so a column reorder fails loudly instead of silently comparing the wrong cell.
    /// </summary>
    private static async Task<(string Scope, HashSet<string> EmployeeNos)> ReadScopeAndEmployeeNosAsync(
        HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");

        var columns = data.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ToList();
        int employeeNoIndex = columns.IndexOf("Employee No");
        employeeNoIndex.Should().BeGreaterThanOrEqualTo(0,
            "the BalanceSummary report must expose an 'Employee No' column for this assertion to mean anything");

        var nos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in data.GetProperty("rows").EnumerateArray())
        {
            var cells = row.GetProperty("cells");
            nos.Add(cells[employeeNoIndex].GetString()!);
        }

        return (data.GetProperty("scope").GetString()!, nos);
    }

    // ── seeding ───────────────────────────────────────────────────────────────────────────────────

    private sealed record SeededOrg(
        string Subdomain, string ManagerEmail, string EmployeeEmail, string HrEmail, string RecruiterEmail,
        string MinimalTeamGrantEmail);

    /// <summary>
    /// Seeds a self-contained tenant with two reporting lines and four personas.
    ///
    /// <para>Every employee gets a <c>Used</c> ledger entry for the seeded leave type, because
    /// BalanceSummary SKIPS a (employee × leave type) pair with no entitlement and no activity. Without it
    /// an empty report would pass the "manager sees no other team" assertion for the wrong reason.</para>
    /// </summary>
    private async Task<SeededOrg> SeedOrgAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenantId = Guid.NewGuid();
        var subdomain = $"e002{Guid.NewGuid():N}"[..14];

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Subdomain = subdomain,
            Name = "ENH-002 Leave Report Scope",
            Status = TenantStatus.Active,
            PlanId = "default",
            DefaultCountryCode = "LK",
            FiscalYearStartMonth = 1, // leave year == calendar year, so ledger LeaveYear 2026 lines up.
        });

        var deptId = Guid.NewGuid();
        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = tenantId, Name = "Engineering", Code = "ENG",
        });

        var jobTitleId = Guid.NewGuid();
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = "Engineer", IsActive = true,
        });

        var leaveTypeId = Guid.NewGuid();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = leaveTypeId, TenantId = tenantId, Name = "Annual Leave", Code = "AL", Color = "#4CAF50",
            AnnualEntitlement = 14, AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All, DisplayOrder = 1, IsActive = true,
        });

        // ── the org chart ────────────────────────────────────────────────────────────────────────
        var mgrOwnId = Guid.NewGuid();
        var otherMgrId = Guid.NewGuid();

        var managerUserId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        // The second team's manager, used by the minimal-grant persona in arm 6.
        var otherMgrUserId = Guid.NewGuid();

        var employees = new[]
        {
            Emp(mgrOwnId, tenantId, deptId, jobTitleId, MgrOwnNo, "Mia", reportsTo: null, userId: managerUserId),
            Emp(Guid.NewGuid(), tenantId, deptId, jobTitleId, MgrReportNo, "Ravi", reportsTo: mgrOwnId, userId: null),
            Emp(otherMgrId, tenantId, deptId, jobTitleId, OtherMgrNo, "Otto", reportsTo: null, userId: otherMgrUserId),
            Emp(Guid.NewGuid(), tenantId, deptId, jobTitleId, OtherEmpNo, "Bea", reportsTo: otherMgrId, userId: employeeUserId),
            Emp(Guid.NewGuid(), tenantId, deptId, jobTitleId, HrEmpNo, "Hana", reportsTo: null, userId: null),
        };
        db.Employees.AddRange(employees);

        foreach (var emp in employees)
        {
            db.LeaveLedgerEntries.Add(new LeaveLedger
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = emp.Id,
                LeaveTypeId = leaveTypeId,
                LeaveYear = ReportYear,
                EntryType = LedgerEntryType.Used,
                Amount = -2m,
                BalanceAfter = 12m,
                OccurredAt = new DateTime(ReportYear, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                Description = "ENH-002 seed",
            });
        }

        // ── personas, each on the REAL built-in permission set ───────────────────────────────────
        var managerEmail = AddPersona(db, tenantId, PermissionCatalog.BuiltInRoles.Manager, managerUserId);
        var employeeEmail = AddPersona(db, tenantId, PermissionCatalog.BuiltInRoles.Employee, employeeUserId);
        var hrEmail = AddPersona(db, tenantId, PermissionCatalog.BuiltInRoles.HRManager, Guid.NewGuid());
        var recruiterEmail = AddPersona(db, tenantId, PermissionCatalog.BuiltInRoles.Recruiter, Guid.NewGuid());

        // A CUSTOM role holding exactly one permission — no Reports.View.Team to fall back on (arm 6).
        var minimalTeamGrantEmail = AddPersona(
            db, tenantId, "ENH-002 Leave Team Reporter", otherMgrUserId,
            permissions: [PermissionCatalog.Leave.ReportsTeam], isBuiltIn: false);

        await db.SaveChangesAsync();
        return new SeededOrg(
            subdomain, managerEmail, employeeEmail, hrEmail, recruiterEmail, minimalTeamGrantEmail);
    }

    private static Employee Emp(
        Guid id, Guid tenantId, Guid deptId, Guid jobTitleId,
        string employeeNo, string firstName, Guid? reportsTo, Guid? userId) => new()
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            EmployeeNo = employeeNo,
            FirstName = firstName,
            LastName = "Test",
            Email = $"{employeeNo.ToLowerInvariant()}.{id:N}@enh002.test",
            DateOfJoining = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DepartmentId = deptId,
            JobTitleId = jobTitleId,
            ReportsToEmployeeId = reportsTo,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
        };

    /// <summary>
    /// Adds a user carrying a tenant role. By default the role's permissions are the genuine built-in
    /// defaults for <paramref name="roleName"/>; pass <paramref name="permissions"/> to grant an exact,
    /// minimal custom set instead. Returns the login email.
    /// </summary>
    private static string AddPersona(
        AppDbContext db, Guid tenantId, string roleName, Guid userId,
        IReadOnlyList<string>? permissions = null, bool isBuiltIn = true)
    {
        var granted = permissions ?? PermissionCatalog.DefaultPermissionsFor(roleName);
        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = roleName,
            IsBuiltIn = isBuiltIn,
            RolePermissions = granted.Select(p => new RolePermission { Permission = p }).ToList(),
        };
        db.Roles.Add(role);

        var email = $"{roleName.Replace(' ', '.').ToLowerInvariant()}-{userId:N}@enh002.test";
        db.Users.Add(new User
        {
            Id = userId,
            Email = email,
            IsActive = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(PersonaPassword, workFactor: 12),
        });

        var membership = new UserTenant
        {
            Id = Guid.NewGuid(),
            UserId = userId,
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

        return email;
    }
}
