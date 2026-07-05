// ============================================================================
// BUG-050 (Attendance, US-ATT-010): dashboard + report READ endpoints were gated by a
// single literal [RequirePermission("Attendance.View.All")]. Managers hold
// Attendance.View.Team (NOT View.All) and, per US-ATT-010 BR-4, are meant to reach the
// dashboard/reports with scope=team — but the single-permission guard 403'd them.
//
// Fix (in AttendanceController): the dashboard/report READ endpoints now list BOTH
// Attendance.View.Team AND Attendance.View.All, and PermissionAuthorizationHandler grants
// on ANY-of match. Admin-only WRITE actions (scheduled-report create/update/delete) stay on
// Attendance.View.All.
//
// This suite mirrors the ISSUE-018 EmployeeDirectoryAuthorizationTests gold-standard pattern:
// it derives the required-permission SET from the ACTUAL controller attribute (via reflection
// through the real PermissionPolicyProvider) and evaluates the real PermissionAuthorizationHandler
// -- so it tracks the controller rather than a hand-copied literal. Pre-fix (View.All only) the
// ViewTeam arms are red; post-fix they are green. If the attribute is ever reverted to only
// Attendance.View.All, the ViewTeam arms fail = bug re-caught.
//
// Representative widened endpoints targeted (2, not all): GetDashboard (the main dashboard KPIs,
// AC-1) and GetLiveBoard (the live per-employee board, AC-2) -- the two US-ATT-010 dashboard READ
// endpoints the fix widened to {View.Team, View.All}. (The report READs -- department-comparison /
// custom / trends -- remain on View.All as the fix landed; see the test report note.)
// ============================================================================

using System.Security.Claims;
using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AttendanceDashboardAuthorizationTests
{
    // The representative widened dashboard READ actions on AttendanceController (BUG-050).
    public static IEnumerable<object[]> WidenedReadEndpoints =>
        new[]
        {
            new object[] { nameof(AttendanceController.GetDashboard) },  // GET /attendance/dashboard (AC-1)
            new object[] { nameof(AttendanceController.GetLiveBoard) },  // GET /attendance/dashboard/live-board (AC-2)
        };

    // --- The endpoint's actual required-permission requirement, resolved end-to-end from the
    //     controller attribute through the real policy provider (tracks the controller, not a literal). ---
    private static async Task<PermissionRequirement> RequirementForAsync(string actionName)
    {
        var method = typeof(AttendanceController).GetMethod(actionName)!;
        var attr = method
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .Single();

        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));
        var policy = await provider.GetPolicyAsync(attr.Policy!);
        policy.Should().NotBeNull($"the {actionName} endpoint must map to a permission policy");

        return policy!.Requirements.OfType<PermissionRequirement>().Single();
    }

    // --- Run a principal holding exactly the given permissions through the real handler. ---
    private static async Task<bool> IsAuthorizedAsync(PermissionRequirement requirement, params string[] heldPermissions)
    {
        var handler = new PermissionAuthorizationHandler(
            Substitute.For<ILogger<PermissionAuthorizationHandler>>());

        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("tenant_id", Guid.NewGuid().ToString()),
        };
        claims.AddRange(heldPermissions.Select(p => new Claim("permissions", p)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);

        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Theory]
    [MemberData(nameof(WidenedReadEndpoints))]
    public async Task AttendanceDashboard_RequiredPermissionSet_IncludesViewTeam_BUG050(string actionName)
    {
        // Pins the fix at the controller boundary: the widened READ endpoint's required-permission set
        // must include Attendance.View.Team (so Managers reach it), not just Attendance.View.All.
        // Pre-fix (set = {Attendance.View.All}) this FAILS; post-fix it passes.
        var requirement = await RequirementForAsync(actionName);

        requirement.Permissions.Should().Contain(
            new[] { "Attendance.View.Team", "Attendance.View.All" },
            $"{actionName} is a dashboard/report READ that Managers (View.Team) and HR (View.All) must both reach (BUG-050)");
    }

    [Theory]
    [MemberData(nameof(WidenedReadEndpoints))]
    public async Task AttendanceDashboard_ViewTeamPrincipal_IsAuthorized_BUG050(string actionName)
    {
        // KEY REGRESSION: Manager persona -- holds Attendance.View.Team, NOT View.All.
        // Pre-fix (requirement = {Attendance.View.All}) this is DENIED -> 403 (the bug).
        // Post-fix (requirement includes Attendance.View.Team) this is AUTHORIZED.
        var requirement = await RequirementForAsync(actionName);

        var authorized = await IsAuthorizedAsync(requirement, "Attendance.View.Team");

        authorized.Should().BeTrue(
            $"a caller holding only Attendance.View.Team (Manager) must reach {actionName} for scope=team (BUG-050)");
    }

    [Theory]
    [MemberData(nameof(WidenedReadEndpoints))]
    public async Task AttendanceDashboard_ViewAllPrincipal_IsAuthorized_BUG050(string actionName)
    {
        // HR persona -- holds Attendance.View.All. The one arm that already worked pre-fix; must stay
        // authorized (no regression from the widening).
        var requirement = await RequirementForAsync(actionName);

        var authorized = await IsAuthorizedAsync(requirement, "Attendance.View.All");

        authorized.Should().BeTrue(
            $"a caller holding Attendance.View.All (HR) must still reach {actionName} (no regression, BUG-050)");
    }

    [Theory]
    [MemberData(nameof(WidenedReadEndpoints))]
    public async Task AttendanceDashboard_NoAttendanceViewPrincipal_IsDenied_BUG050(string actionName)
    {
        // NEGATIVE ARM (proves the suite isn't "always succeeds"): a caller with an unrelated permission
        // and no Attendance.View.* must NOT reach the dashboard/report. Passes pre- and post-fix; it
        // exists so a fix that opened the endpoint would be caught.
        var requirement = await RequirementForAsync(actionName);

        var authorized = await IsAuthorizedAsync(requirement, "Attendance.CheckIn");

        authorized.Should().BeFalse(
            $"a caller with only Attendance.CheckIn (self clock-in, no View.*) must be denied {actionName} — the fix widens roles, it does not open the endpoint (BUG-050)");
    }

    [Fact]
    public async Task AttendanceDashboard_PreFixViewAllOnlyRequirement_WouldDenyViewTeamPrincipal_BUG050()
    {
        // Pins the ROOT CAUSE through the real handler: the OLD guard was a single literal
        // {Attendance.View.All}. A View.Team-only principal (Manager) fails ANY-of match against that
        // set -> 403. This is the exact pre-fix denial the controller change removes; kept so the
        // regression is self-documenting even to a reader who never sees the old controller source.
        var preFixRequirement = new PermissionRequirement("Attendance.View.All");
        var currentRequirement = await RequirementForAsync(nameof(AttendanceController.GetDashboard));

        var authorizedUnderOldGuard = await IsAuthorizedAsync(preFixRequirement, "Attendance.View.Team");
        var authorizedUnderNewGuard = await IsAuthorizedAsync(currentRequirement, "Attendance.View.Team");

        authorizedUnderOldGuard.Should().BeFalse(
            "the old single Attendance.View.All guard 403'd View.Team holders (the BUG-050 defect)");
        authorizedUnderNewGuard.Should().BeTrue(
            "the current dashboard guard authorizes View.Team holders (the BUG-050 fix)");
    }
}
