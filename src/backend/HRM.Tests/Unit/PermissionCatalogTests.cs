// ============================================================================
// US-AUTH-006: RBAC Unit Tests - Permission Catalog
// Follow-up: Integration tests with Testcontainers (database-backed) are NOT
// included in this pass. Add them in a future sprint when the test infra matures.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Authorization;

namespace HRM.Tests.Unit;

/// <summary>
/// Tests the static PermissionCatalog source of truth.
/// </summary>
public sealed class PermissionCatalogTests
{
    [Fact]
    public void AllPermissions_ShouldContainAtLeast40Entries()
    {
        PermissionCatalog.AllPermissions.Should().HaveCountGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void AllPermissions_ShouldFollowModuleActionPattern()
    {
        foreach (var perm in PermissionCatalog.AllPermissions)
        {
            var parts = perm.Split('.');
            parts.Length.Should().BeGreaterThanOrEqualTo(2, $"Permission '{perm}' must have at least Module.Action");
            parts[0].Should().NotBeNullOrWhiteSpace($"Module segment of '{perm}' should not be empty");
            parts[1].Should().NotBeNullOrWhiteSpace($"Action segment of '{perm}' should not be empty");
        }
    }

    [Fact]
    public void AllPermissions_ShouldContainNoDuplicates()
    {
        PermissionCatalog.AllPermissions.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("Payroll.View", true)]
    [InlineData("Leave.Approve.Team", true)]
    [InlineData("Employee.View.All", true)]
    [InlineData("NonExistent.Permission", false)]
    [InlineData("", false)]
    [InlineData("Payroll", false)]
    public void IsValid_ShouldReturnCorrectResult(string permission, bool expected)
    {
        PermissionCatalog.IsValid(permission).Should().Be(expected);
    }

    [Fact]
    public void ByModule_ShouldGroupAllPermissions()
    {
        var totalInGroups = PermissionCatalog.ByModule.Values.SelectMany(v => v).Count();
        totalInGroups.Should().Be(PermissionCatalog.AllPermissions.Count);
    }

    [Fact]
    public void ByModule_ShouldContainExpectedModules()
    {
        PermissionCatalog.ByModule.Keys.Should().Contain("Employee");
        PermissionCatalog.ByModule.Keys.Should().Contain("Leave");
        PermissionCatalog.ByModule.Keys.Should().Contain("Payroll");
        PermissionCatalog.ByModule.Keys.Should().Contain("Roles");
    }

    [Fact]
    public void BuiltInRoles_ShouldContainExpectedRoles()
    {
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Tenant Owner");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Tenant Admin");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("HR Officer");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Manager");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Employee");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Recruiter");
        PermissionCatalog.BuiltInRoles.All.Should().Contain("Auditor");
    }

    [Fact]
    public void DefaultPermissionsFor_TenantOwner_ShouldReturnAllPermissions()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.TenantOwner);
        perms.Should().BeEquivalentTo(PermissionCatalog.AllPermissions);
    }

    [Fact]
    public void DefaultPermissionsFor_Employee_ShouldReturnSubset()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Employee);
        perms.Should().NotBeEmpty();
        perms.Should().Contain("Leave.Apply");
        perms.Should().Contain("Employee.View.Own");
        perms.Should().NotContain("Roles.Manage");
        perms.Should().NotContain("Payroll.Run");
    }

    [Fact]
    public void DefaultPermissionsFor_UnknownRole_ShouldReturnEmpty()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor("NonExistentRole");
        perms.Should().BeEmpty();
    }

    [Fact]
    public void SystemRoles_ShouldContainExpectedRoles()
    {
        PermissionCatalog.SystemRoles.All.Should().Contain("System Super Admin");
        PermissionCatalog.SystemRoles.All.Should().Contain("System Support");
        PermissionCatalog.SystemRoles.All.Should().Contain("System Billing");
        PermissionCatalog.SystemRoles.All.Should().Contain("System Compliance");
    }

    // ── US-ADM-008 (FR-7/BR-2): Auditor is READ-ONLY on the audit log — it can VIEW but NOT EXPORT ──

    [Fact]
    public void DefaultPermissionsFor_Auditor_HasAuditViewButNotExport()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Auditor);

        perms.Should().Contain(PermissionCatalog.Audit.View);
        perms.Should().NotContain(PermissionCatalog.Audit.Export);
    }

    [Fact]
    public void DefaultPermissionsFor_TenantAdmin_HasBothAuditViewAndExport()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.TenantAdmin);

        perms.Should().Contain(PermissionCatalog.Audit.View);
        perms.Should().Contain(PermissionCatalog.Audit.Export);
    }

    // ── BUG-036: Leave.ManageLop must be granted to the HR-administrative roles, not just ──
    // ── TenantOwner (via AllPermissions). Otherwise the entire LOP surface is unreachable. ──

    [Theory]
    [InlineData("Tenant Admin")]
    [InlineData("HR Manager")]
    [InlineData("HR Officer")]
    public void DefaultPermissionsFor_HrAdminRoles_HaveLeaveManageLop(string roleName)
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(roleName);

        perms.Should().Contain(PermissionCatalog.Leave.ManageLop,
            $"role '{roleName}' manages Leave and must be able to manage LOP (BUG-036)");
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Employee")]
    public void DefaultPermissionsFor_NonHrRoles_DoNotHaveLeaveManageLop(string roleName)
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(roleName);

        perms.Should().NotContain(PermissionCatalog.Leave.ManageLop);
    }

    // ── BUG-027 / BUG-034: HR Officer must be able to CONFIGURE leave policy — i.e. set up leave ──
    // ── entitlements (BUG-027) and run the carry-forward PREVIEW (BUG-034). Both endpoints are gated ──
    // ── by Leave.ConfigurePolicy, which was granted to Tenant Owner / Tenant Admin / HR Manager but ──
    // ── NOT to HR Officer, so an HR Officer was 403'd from the entitlement-config + carry-forward     ──
    // ── preview surfaces. Fix: add Leave.ConfigurePolicy to the HR Officer role map. These tests key  ──
    // ── on the REAL catalog role→permission map (DefaultPermissionsFor), so pre-fix the HR Officer arm ──
    // ── is red and post-fix it is green; the negative arm proves the fix is not a blanket grant.       ──

    [Fact]
    public void HrOfficer_HasLeaveConfigurePolicy_BUG027_034()
    {
        // KEY REGRESSION: pre-fix HR Officer's default permission set LACKS Leave.ConfigurePolicy -> red.
        // Post-fix it is present -> green. Keyed on the real catalog map, not a hand-copied literal.
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.HROfficer);

        perms.Should().Contain(PermissionCatalog.Leave.ConfigurePolicy,
            "HR Officer configures leave entitlements (BUG-027) and runs the carry-forward preview (BUG-034)");
    }

    [Theory]
    [InlineData("Tenant Admin")]
    [InlineData("HR Manager")]
    [InlineData("HR Officer")]
    public void DefaultPermissionsFor_HrAdminRoles_HaveLeaveConfigurePolicy_BUG027_034(string roleName)
    {
        // The full set of HR-administrative roles that must hold Leave.ConfigurePolicy. Tenant Admin /
        // HR Manager pass both pre- and post-fix; HR Officer is the arm that flips red -> green.
        var perms = PermissionCatalog.DefaultPermissionsFor(roleName);

        perms.Should().Contain(PermissionCatalog.Leave.ConfigurePolicy,
            $"role '{roleName}' administers leave and must configure leave policy (BUG-027/BUG-034)");
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Employee")]
    [InlineData("Recruiter")]
    [InlineData("Auditor")]
    public void DefaultPermissionsFor_NonLeaveAdminRoles_DoNotHaveLeaveConfigurePolicy_BUG027_034(string roleName)
    {
        // NEGATIVE ARM (proves the fix is not a blanket grant): roles that do NOT administer leave
        // policy must still NOT hold Leave.ConfigurePolicy. Passes both pre- and post-fix; it exists so
        // a fix that over-granted the permission would be caught.
        var perms = PermissionCatalog.DefaultPermissionsFor(roleName);

        perms.Should().NotContain(PermissionCatalog.Leave.ConfigurePolicy,
            $"role '{roleName}' does not configure leave policy — the fix must add it to HR Officer only, not broadly (BUG-027/BUG-034)");
    }

    // ── BUG-060 / BUG-071 / BUG-077: HR Officer is the named payroll operator per US-PAY-001 (persona: ──
    // ── Tenant Admin / HR Officer) and US-PAY-003 (persona: HR Officer, "has Payroll.Run"), yet the seed ──
    // ── granted HR Officer ZERO Payroll.* permissions → 403 on salary-component config (BUG-060), payroll ──
    // ── run (BUG-071), and payroll reports/analytics (BUG-077). Fix: grant Payroll.View/Run/Configure/    ──
    // ── Export to HR Officer — but NOT Payroll.Approve (separation of duties) nor Payroll.ViewSensitive    ──
    // ── (unmasked bank PII, deliberately HR-Manager+ only per PermissionCatalog.Payroll.ViewSensitive doc).──

    [Fact]
    public void HrOfficer_HasPayrollRunConfigureExport_BUG060_071_077()
    {
        // KEY REGRESSION: pre-fix HR Officer's default set has NO Payroll.* -> red on all three.
        // Keyed on the real catalog map (DefaultPermissionsFor), not a hand-copied literal.
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.HROfficer);

        perms.Should().Contain(PermissionCatalog.Payroll.Configure, "HR Officer configures salary components/structures (BUG-060)");
        perms.Should().Contain(PermissionCatalog.Payroll.Run, "HR Officer runs payroll (BUG-071, US-PAY-003)");
        perms.Should().Contain(PermissionCatalog.Payroll.Export, "HR Officer generates/exports payroll reports (BUG-077)");
        perms.Should().Contain(PermissionCatalog.Payroll.View, "HR Officer views payroll they run/configure");
    }

    [Fact]
    public void HrOfficer_DoesNotHavePayrollApproveOrViewSensitive_BUG060_071_077()
    {
        // NEGATIVE ARM (proves the fix is not a blanket grant): HR Officer must still NOT hold
        // Payroll.Approve (separation of duties — a different role approves what HR Officer runs) nor
        // Payroll.ViewSensitive (unmasked bank account PII — HR Manager / Tenant Admin / Owner only).
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.HROfficer);

        perms.Should().NotContain(PermissionCatalog.Payroll.Approve, "separation of duties: HR Officer runs but does not approve payroll");
        perms.Should().NotContain(PermissionCatalog.Payroll.ViewSensitive, "HR Officer is not trusted with unmasked bank PII");
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Employee")]
    [InlineData("Recruiter")]
    public void DefaultPermissionsFor_NonPayrollRoles_DoNotHavePayrollRun_BUG060_071_077(string roleName)
    {
        // The Payroll grant must land on HR Officer only, not broadly. Non-payroll roles stay clear.
        var perms = PermissionCatalog.DefaultPermissionsFor(roleName);

        perms.Should().NotContain(PermissionCatalog.Payroll.Run,
            $"role '{roleName}' does not run payroll — the fix must add Payroll.* to HR Officer only");
    }

    // ── DEC-1: dedicated report-scope taxonomy. The mapping is behavior-preserving: roles that held an
    // ── org-wide read (Employee/Leave/Attendance.View.All) get Reports.View.All; the Manager (team read)
    // ── gets Reports.View.Team; the Employee (self only) gets neither. ────────────────────────────────

    [Theory]
    [InlineData("Tenant Admin")]
    [InlineData("HR Manager")]
    [InlineData("HR Officer")]
    [InlineData("Auditor")]
    public void DefaultPermissionsFor_OrgWideReadRoles_HaveReportsViewAll_DEC1(string roleName)
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(roleName);
        perms.Should().Contain(PermissionCatalog.Reports.ViewAll,
            $"role '{roleName}' held an org-wide *.View.All read, so DEC-1 preserves its whole-tenant report scope");
    }

    [Fact]
    public void DefaultPermissionsFor_Manager_HasReportsViewTeam_NotViewAll_DEC1()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Manager);
        perms.Should().Contain(PermissionCatalog.Reports.ViewTeam,
            "Manager held team reads (*.View.Team), so DEC-1 preserves its team report scope");
        perms.Should().NotContain(PermissionCatalog.Reports.ViewAll,
            "Manager never held an org-wide read, so it must NOT gain org-wide report scope");
    }

    // ── ISSUE-137 (BR-5): department-scoped recruitment dashboard permission. ──────────────────────────

    [Fact]
    public void Reports_ViewDepartment_IsInCatalog_ISSUE137()
    {
        PermissionCatalog.IsValid(PermissionCatalog.Reports.ViewDepartment).Should().BeTrue(
            "Reports.View.Department must be registered in AllPermissions (BR-5 department-scoped dashboard)");
        PermissionCatalog.Reports.ViewDepartment.Should().Be("Reports.View.Department");
    }

    [Fact]
    public void DefaultPermissionsFor_Manager_HasReportsViewDepartment_ISSUE137()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Manager);
        perms.Should().Contain(PermissionCatalog.Reports.ViewDepartment,
            "the Manager persona owns the department-scoped recruitment dashboard (BR-5)");
    }

    [Fact]
    public void DefaultPermissionsFor_Recruiter_HasNoReportsViewDepartment_ISSUE137()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Recruiter);
        perms.Should().NotContain(PermissionCatalog.Reports.ViewDepartment,
            "Recruiter keeps full recruitment access via Recruitment.View — it must NOT gain the dept-scoped perm");
    }

    [Fact]
    public void DefaultPermissionsFor_Employee_HasNeitherReportsScope_DEC1()
    {
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Employee);
        perms.Should().NotContain(PermissionCatalog.Reports.ViewAll);
        perms.Should().NotContain(PermissionCatalog.Reports.ViewTeam);
    }

    [Fact]
    public void DefaultPermissionsFor_Recruiter_HasNoReportsScope_DEC1()
    {
        // Recruiter keeps its own Employee.View.All but is deliberately NOT granted cross-module report scope:
        // it holds no Reports.View endpoint gate and org-wide report access is not part of the recruiter persona.
        var perms = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.Recruiter);
        perms.Should().Contain(PermissionCatalog.Employee.ViewAll, "Recruiter's own module perm is unchanged");
        perms.Should().NotContain(PermissionCatalog.Reports.ViewAll,
            "Recruiter must not gain org-wide report scope — it is not part of the recruiter persona");
        perms.Should().NotContain(PermissionCatalog.Reports.ViewTeam);
    }

    [Fact]
    public void DefaultPermissionsFor_AllBuiltInRoles_ShouldContainOnlyValidPermissions()
    {
        foreach (var roleName in PermissionCatalog.BuiltInRoles.All)
        {
            var perms = PermissionCatalog.DefaultPermissionsFor(roleName);
            foreach (var perm in perms)
            {
                PermissionCatalog.IsValid(perm).Should().BeTrue(
                    $"Permission '{perm}' for role '{roleName}' should be in the catalog");
            }
        }
    }
}
