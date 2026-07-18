// ============================================================================
// ISSUE-267 (US-ADM-011c FR-12) — the workflow-instance step-chain read endpoint
// (GET /api/v1/tenant/workflow-instances/{instanceId}) was [Authorize]-only, so ANY
// authenticated tenant user holding an instance GUID could read its full step chain
// (approver identities, decisions, comments). The fix gates the read behind
// Tenant.ViewWorkflows — the same permission that gates the workflow-definition reads.
//
// Side-effect-free reflection guard: RequirePermission maps to a single authorization
// policy "Permission:<perms>", so asserting the policy names Tenant.ViewWorkflows fails
// the instant the gate is removed. (The decide actions keep their dynamic per-approver authz.)
// ============================================================================

using System.Reflection;
using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Infrastructure.Identity;

namespace HRM.Tests.Unit;

public sealed class WorkflowInstanceReadPermissionAttributeTests
{
    [Fact]
    public void WorkflowInstancesController_Get_RequiresViewWorkflows_ISSUE267()
    {
        var method = typeof(WorkflowInstancesController)
            .GetMethod(nameof(WorkflowInstancesController.Get), BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull("the step-chain read action must exist");

        var attribute = method!.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).SingleOrDefault();
        attribute.Should().NotBeNull(
            "the step-chain read must be permission-gated, not open to any authenticated tenant user (ISSUE-267)");
        attribute!.Policy.Should().Contain(
            "Tenant.ViewWorkflows", "a random tenant user must not read arbitrary workflow instance step chains");
    }
}
