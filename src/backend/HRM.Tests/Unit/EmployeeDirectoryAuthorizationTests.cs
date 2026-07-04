// ============================================================================
// ISSUE-018 (HIGH, Core HR, US-CHR-003): Directory endpoint role-based access.
//
// Regression for TC-CHR-138 (role-based directory visibility / access).
//
// Bug: GET /api/v1/tenant/employees/directory was gated by a single literal
//   [RequirePermission("Employee.View.Own")]. HR Officer and Tenant Admin hold
//   Employee.View.All, and Managers hold Employee.View.Team -- both strict
//   supersets of View.Own -- but NOT the literal Employee.View.Own, so they were
//   403'd even though GetEmployeeDirectoryQueryHandler.ResolveVisibility already
//   maps View.All -> Full visibility. Fix (in EmployeesController): the attribute
//   now lists all three permissions and PermissionAuthorizationHandler grants on
//   ANY-of match.
//
// This suite derives the required-permission SET from the actual GetDirectory
// controller attribute (via reflection + the real PermissionPolicyProvider) and
// evaluates the real PermissionAuthorizationHandler -- so it tracks the controller
// rather than a hand-copied literal. If the attribute is ever reverted to only
// Employee.View.Own, the View.All-only / View.Team-only arms fail = bug re-caught.
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

public sealed class EmployeeDirectoryAuthorizationTests
{
    // --- The directory endpoint's actual required-permission requirement, resolved
    //     end-to-end from the controller attribute through the real policy provider. ---
    private static async Task<PermissionRequirement> DirectoryRequirementAsync()
    {
        var method = typeof(EmployeesController).GetMethod(nameof(EmployeesController.GetDirectory))!;
        var attr = method
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .Single();

        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));
        var policy = await provider.GetPolicyAsync(attr.Policy!);
        policy.Should().NotBeNull("the directory endpoint must map to a permission policy");

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

    [Fact]
    public async Task Directory_RequiredPermissionSet_TracksControllerAndIncludesViewAll_ISSUE018()
    {
        // The directory's required-permission set is the guard ISSUE-018 tightened.
        // Asserting its contents documents/pins the fix at the controller boundary.
        var requirement = await DirectoryRequirementAsync();

        requirement.Permissions.Should().Contain(
            new[] { "Employee.View.Own", "Employee.View.Team", "Employee.View.All" },
            "HR Officer/Tenant Admin (View.All) and Managers (View.Team) must satisfy the directory guard, not just View.Own holders");
    }

    [Fact]
    public async Task Directory_ViewAllOnlyPrincipal_IsAuthorized_ISSUE018()
    {
        // KEY REGRESSION: HR Officer / Tenant Admin persona -- holds View.All, NOT View.Own.
        // Pre-fix (requirement = {Employee.View.Own}) this is DENIED -> 403 (the bug).
        // Post-fix (requirement includes Employee.View.All) this is AUTHORIZED.
        var requirement = await DirectoryRequirementAsync();

        var authorized = await IsAuthorizedAsync(requirement, "Employee.View.All");

        authorized.Should().BeTrue(
            "a caller holding only Employee.View.All (HR Officer/Tenant Admin) must reach the directory (ISSUE-018)");
    }

    [Fact]
    public async Task Directory_ViewTeamOnlyPrincipal_IsAuthorized_ISSUE018()
    {
        // Manager persona -- holds View.Team, NOT View.Own. Pre-fix: DENIED. Post-fix: AUTHORIZED.
        var requirement = await DirectoryRequirementAsync();

        var authorized = await IsAuthorizedAsync(requirement, "Employee.View.Team");

        authorized.Should().BeTrue(
            "a caller holding only Employee.View.Team (Manager) must reach the directory (ISSUE-018)");
    }

    [Fact]
    public async Task Directory_ViewOwnOnlyPrincipal_IsAuthorized_ISSUE018()
    {
        // Regular Employee persona -- the one role that already worked pre-fix. Must stay authorized.
        var requirement = await DirectoryRequirementAsync();

        var authorized = await IsAuthorizedAsync(requirement, "Employee.View.Own");

        authorized.Should().BeTrue(
            "a caller holding Employee.View.Own must still reach the directory (no regression from ISSUE-018 fix)");
    }

    [Fact]
    public async Task Directory_NonEmployeeViewPermissionPrincipal_IsDenied_ISSUE018()
    {
        // NEGATIVE ARM (proves the suite isn't "always succeeds"): a caller with an
        // unrelated permission and no Employee.View.* must NOT reach the directory.
        var requirement = await DirectoryRequirementAsync();

        var authorized = await IsAuthorizedAsync(requirement, "Leave.View.Own");

        authorized.Should().BeFalse(
            "a caller with no Employee.View.* permission must be denied the directory (fix widens roles, it does not open the endpoint)");
    }

    [Fact]
    public async Task Directory_PreFixSingleViewOwnRequirement_WouldDenyViewAllPrincipal_ISSUE018()
    {
        // Pins the ROOT CAUSE of ISSUE-018 through the real handler: the OLD guard was a
        // single literal {Employee.View.Own}. A View.All-only principal (HR Officer / Tenant
        // Admin) fails ANY-of match against that set -> 403. This is the exact pre-fix denial
        // the controller change removes; kept so the regression is self-documenting even to a
        // reader who never sees the old controller source.
        var preFixRequirement = new PermissionRequirement("Employee.View.Own");

        var authorizedUnderOldGuard = await IsAuthorizedAsync(preFixRequirement, "Employee.View.All");
        var authorizedUnderNewGuard = await IsAuthorizedAsync(await DirectoryRequirementAsync(), "Employee.View.All");

        authorizedUnderOldGuard.Should().BeFalse("the old single Employee.View.Own guard 403'd View.All holders (the ISSUE-018 bug)");
        authorizedUnderNewGuard.Should().BeTrue("the current directory guard authorizes View.All holders (the ISSUE-018 fix)");
    }
}
