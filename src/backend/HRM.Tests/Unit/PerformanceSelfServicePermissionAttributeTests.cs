// ============================================================================
// US-PRF-008 / US-PRF-009 — self-service permission-gate guards for the Performance module.
//
// ISSUE-133: GET /api/v1/tenant/performance/pips (PipController.List) must admit Performance.Read.Self so an
//            employee/mentor can reach their OWN PIP list (PipService.ListAsync already scopes the rows). Pre-fix
//            the action only allowed Review.Team/Review.All, 403-ing a Read.Self employee before the service ran.
// ISSUE-141: POST /api/v1/tenant/performance/goals/{goalId}/comments (GoalProgressController.AddComment) must admit
//            Performance.Read.Self so the goal OWNER can reply on their own thread (GoalProgressService scopes a
//            Read.Self caller to goals they own). Pre-fix only Review.Team/Review.All were allowed.
//
// These are side-effect-free reflection guards: the RequirePermission attribute maps to a single authorization
// policy "Permission:<comma-joined perms>", so asserting the policy string names Read.Self (alongside the manager/
// HR perms) fails the instant the permission is dropped from the action. The per-row visibility/ownership scoping
// is exercised by PipServiceTests / GoalProgressServiceTests.
// ============================================================================

using System.Reflection;
using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Infrastructure.Identity;

namespace HRM.Tests.Unit;

public sealed class PerformanceSelfServicePermissionAttributeTests
{
    private const string ReadSelf = "Performance.Read.Self";
    private const string ReviewTeam = "Performance.Review.Team";
    private const string ReviewAll = "Performance.Review.All";

    // ISSUE-133: the PIP list action admits Read.Self (plus the manager/HR perms).
    [Fact]
    public void PipController_List_requires_ReadSelf_ISSUE133()
    {
        var policy = RequirePermissionPolicy(typeof(PipController), nameof(PipController.List));
        policy.Should().Contain(ReadSelf, "an employee/mentor must reach their own PIP list (ISSUE-133)");
        policy.Should().Contain(ReviewTeam).And.Contain(ReviewAll, "the manager/HR gates must remain");
    }

    // ISSUE-141: the goal-comment action admits Read.Self (plus the manager/HR perms).
    [Fact]
    public void GoalProgressController_AddComment_requires_ReadSelf_ISSUE141()
    {
        var policy = RequirePermissionPolicy(typeof(GoalProgressController), nameof(GoalProgressController.AddComment));
        policy.Should().Contain(ReadSelf, "the goal owner must be able to reply on their own thread (ISSUE-141)");
        policy.Should().Contain(ReviewTeam).And.Contain(ReviewAll, "the manager/HR gates must remain");
    }

    private static string RequirePermissionPolicy(Type controller, string methodName)
    {
        var method = controller.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull($"{controller.Name}.{methodName} must exist");
        var attribute = method!.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).SingleOrDefault();
        attribute.Should().NotBeNull($"{controller.Name}.{methodName} must carry a [RequirePermission]");
        attribute!.Policy.Should().NotBeNullOrEmpty();
        return attribute.Policy!;
    }
}
