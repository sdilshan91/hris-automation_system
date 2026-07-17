// ============================================================================
// US-PAY-008: Payroll approval-workflow service unit tests.
//
// Covers the additive state machine (BR-4) and its guard rails:
//   - Submit: ReviewPending -> AwaitingApproval, writes a Submitted history row (AC-1).
//   - Invalid transitions are rejected with 409 invalid_transition:
//       * Approve when not AwaitingApproval.
//       * Submit when not ReviewPending/Rejected.
//       * Finalize directly from ReviewPending (BR-1 — approval required first).
//   - Finalize is terminal: a Finalized run cannot be re-finalized (BR-6).
//   - Reject requires a >=10 char reason (AC-3); a short reason fails 400 reason_required.
//   - Maker-checker (BR-5): the submitter cannot approve their own run when the tenant has >=2 eligible
//     approvers; the small-team exception (<2) allows it.
//   - Multi-step routing (AC-4): a 2-step submit stays AwaitingApproval after the first approval and only
//     becomes Approved after the second.
//   - Re-submit after rejection (BR-3): starts a NEW workflow instance id.
//
// Uses the EF Core InMemory provider through the service (mirrors RegularizationApprovalServiceTests).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class PayrollApprovalServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _hrUserId = Guid.NewGuid();      // the maker (submitter)
    private readonly Guid _financeUserId = Guid.NewGuid(); // a separate approver
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    public PayrollApprovalServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private PayrollApprovalService Service(Guid actingUserId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(actingUserId);
        var notifications = Substitute.For<IPayrollNotificationService>();
        var logger = Substitute.For<ILogger<PayrollApprovalService>>();
        return new PayrollApprovalService(Db(), _tenantContext, currentUser, notifications, Substitute.For<IPayrollAuditLogger>(), logger);
    }

    private async Task<Guid> SeedRunAsync(PayrollRunStatus status, Guid? submittedBy = null,
        int? step = null, int? totalSteps = null, Guid? instanceId = null)
    {
        using var db = Db();
        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = _tenantId, PayMonth = 5, PayYear = 2026,
            Status = status, InitiatedBy = _hrUserId, InitiatedAt = DateTime.UtcNow,
            TotalEmployees = 2, ProcessedEmployees = 2, TotalGross = 80_000m, TotalNet = 80_000m,
            SubmittedBy = submittedBy,
            CurrentApprovalStep = step,
            TotalApprovalSteps = totalSteps,
            CurrentWorkflowInstanceId = instanceId,
        });
        await db.SaveChangesAsync();
        return runId;
    }

    /// <summary>Seeds N tenant users each holding a role that grants Payroll.Approve (eligible approvers, BR-5).</summary>
    private async Task SeedEligibleApproversAsync(params Guid[] userIds)
    {
        using var db = Db();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = _tenantId, Name = "Approver" });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Payroll.Approve });
        foreach (var uid in userIds)
        {
            var utId = Guid.NewGuid();
            db.UserTenants.Add(new UserTenant { Id = utId, UserId = uid, TenantId = _tenantId, Status = UserTenantStatus.Active });
            db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a tenant role that grants Payroll.Approve and assigns it to the given users (BUG-076).</summary>
    private async Task<Guid> SeedApproverRoleAsync(string name, params Guid[] members)
    {
        using var db = Db();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = _tenantId, Name = name });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Payroll.Approve });
        foreach (var uid in members)
        {
            var utId = Guid.NewGuid();
            db.UserTenants.Add(new UserTenant { Id = utId, UserId = uid, TenantId = _tenantId, Status = UserTenantStatus.Active });
            db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        }
        await db.SaveChangesAsync();
        return roleId;
    }

    /// <summary>Seeds the tenant's payroll-approval step → role config directly (BUG-076).</summary>
    private async Task SeedStepConfigAsync(params (int Step, Guid RoleId)[] steps)
    {
        using var db = Db();
        foreach (var (step, roleId) in steps)
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StepNumber = step, RoleId = roleId,
            });
        await db.SaveChangesAsync();
    }

    // ── Submit (AC-1) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_FromReviewPending_TransitionsToAwaitingApproval_AndWritesHistory()
    {
        var runId = await SeedRunAsync(PayrollRunStatus.ReviewPending);

        var result = await Service(_hrUserId).SubmitForApprovalAsync(runId, null, "ready", "1.2.3.4");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(PayrollRunStatus.AwaitingApproval));
        result.Value.Action.Should().Be(PayrollApprovalAction.Submitted);

        using var db = Db();
        var history = await db.PayrollApprovalHistories.SingleAsync(h => h.PayrollRunId == runId);
        history.Action.Should().Be(PayrollApprovalAction.Submitted);
        history.IpAddress.Should().Be("1.2.3.4");
    }

    // ── Invalid transitions (state machine) ────────────────────────────────────

    [Fact]
    public async Task Approve_WhenNotAwaitingApproval_Returns409InvalidTransition()
    {
        var runId = await SeedRunAsync(PayrollRunStatus.ReviewPending);

        var result = await Service(_financeUserId).ApproveAsync(runId, null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("invalid_transition");
    }

    [Fact]
    public async Task Submit_WhenNotReviewPendingOrRejected_Returns409()
    {
        var runId = await SeedRunAsync(PayrollRunStatus.Queued);

        var result = await Service(_hrUserId).SubmitForApprovalAsync(runId, null, null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("invalid_transition");
    }

    [Fact]
    public async Task Finalize_DirectlyFromReviewPending_IsBlocked_BR1()
    {
        var runId = await SeedRunAsync(PayrollRunStatus.ReviewPending);

        var result = await Service(_hrUserId).FinalizeAsync(runId, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("approval_required");

        using var db = Db();
        var run = await db.PayrollRuns.SingleAsync(r => r.Id == runId);
        run.Status.Should().Be(PayrollRunStatus.ReviewPending); // unchanged
    }

    [Fact]
    public async Task Finalize_FromApproved_Succeeds_AndIsTerminal_BR6()
    {
        var runId = await SeedRunAsync(PayrollRunStatus.Approved);

        var first = await Service(_hrUserId).FinalizeAsync(runId, null);
        first.IsSuccess.Should().BeTrue();
        first.Value!.Status.Should().Be(nameof(PayrollRunStatus.Finalized));

        // BR-6: re-finalizing a Finalized run is rejected.
        var second = await Service(_hrUserId).FinalizeAsync(runId, null);
        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("already_finalized");
    }

    // ── Reject requires a reason (AC-3) ────────────────────────────────────────

    [Fact]
    public async Task Reject_WithShortReason_Fails400()
    {
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 1, instanceId: Guid.NewGuid());

        var result = await Service(_financeUserId).RejectAsync(runId, "bad", null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("reason_required");
    }

    [Fact]
    public async Task Reject_WithReason_TransitionsToRejected_AndStoresReason()
    {
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 1, instanceId: Guid.NewGuid());

        var result = await Service(_financeUserId).RejectAsync(runId, "Net salary looks wrong for dept A.", null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(PayrollRunStatus.Rejected));

        using var db = Db();
        var run = await db.PayrollRuns.SingleAsync(r => r.Id == runId);
        run.RejectionReason.Should().Be("Net salary looks wrong for dept A.");
        var history = await db.PayrollApprovalHistories.SingleAsync(h => h.PayrollRunId == runId);
        history.Action.Should().Be(PayrollApprovalAction.Rejected);
        history.Comments.Should().Be("Net salary looks wrong for dept A.");
    }

    // ── Maker-checker (BR-5) ───────────────────────────────────────────────────

    [Fact]
    public async Task Approve_BySubmitter_WithTwoEligibleApprovers_IsBlocked_SelfApproval()
    {
        await SeedEligibleApproversAsync(_hrUserId, _financeUserId); // 2 eligible approvers
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 1, instanceId: Guid.NewGuid());

        var result = await Service(_hrUserId).ApproveAsync(runId, null, null); // submitter approves own run

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("self_approval");
    }

    [Fact]
    public async Task Approve_BySubmitter_WithSingleEligibleApprover_IsAllowed_SmallTeamException()
    {
        await SeedEligibleApproversAsync(_hrUserId); // only 1 eligible approver
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 1, instanceId: Guid.NewGuid());

        var result = await Service(_hrUserId).ApproveAsync(runId, null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(PayrollRunStatus.Approved));
    }

    [Fact]
    public async Task Approve_ByDifferentApprover_Succeeds()
    {
        await SeedEligibleApproversAsync(_hrUserId, _financeUserId);
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 1, instanceId: Guid.NewGuid());

        var result = await Service(_financeUserId).ApproveAsync(runId, "looks good", null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(PayrollRunStatus.Approved));

        using var db = Db();
        var run = await db.PayrollRuns.SingleAsync(r => r.Id == runId);
        run.ApprovedBy.Should().Be(_financeUserId);
    }

    // ── Multi-step routing (AC-4) ──────────────────────────────────────────────

    [Fact]
    public async Task Approve_MultiStep_StaysAwaitingUntilAllStepsApproved()
    {
        var secondApprover = Guid.NewGuid(); // BUG-076: step 2 must be a DIFFERENT person (separation of duties).
        var instanceId = Guid.NewGuid();
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 2, instanceId: instanceId);

        // First approval — advances to step 2, still AwaitingApproval.
        var first = await Service(_financeUserId).ApproveAsync(runId, null, null);
        first.IsSuccess.Should().BeTrue();
        first.Value!.Status.Should().Be(nameof(PayrollRunStatus.AwaitingApproval));
        first.Value.CurrentApprovalStep.Should().Be(2);

        // Second approval by a DISTINCT approver — all steps complete, run becomes Approved.
        var second = await Service(secondApprover).ApproveAsync(runId, null, null);
        second.IsSuccess.Should().BeTrue();
        second.Value!.Status.Should().Be(nameof(PayrollRunStatus.Approved));

        using var db = Db();
        var historyCount = await db.PayrollApprovalHistories.CountAsync(h => h.PayrollRunId == runId);
        historyCount.Should().Be(2); // one per approval step
    }

    // ── BUG-076: distinct-person separation of duties (AC-4) ───────────────────

    [Fact]
    public async Task Approve_MultiStep_SameUserBothSteps_IsBlocked_DistinctApproverRequired()
    {
        // The BUG-076 repro: previously the SAME user could approve step 1 AND step 2.
        var instanceId = Guid.NewGuid();
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 2, instanceId: instanceId);

        var first = await Service(_financeUserId).ApproveAsync(runId, null, null);
        first.IsSuccess.Should().BeTrue();
        first.Value!.CurrentApprovalStep.Should().Be(2);

        // Same user tries step 2 -> now blocked 403 distinct_approver_required (was previously allowed).
        var second = await Service(_financeUserId).ApproveAsync(runId, null, null);
        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(403);
        second.ErrorCode.Should().Be("distinct_approver_required");

        // Run stays AwaitingApproval at step 2; only ONE Approved history row was recorded.
        using var db = Db();
        var run = await db.PayrollRuns.SingleAsync(r => r.Id == runId);
        run.Status.Should().Be(PayrollRunStatus.AwaitingApproval);
        run.CurrentApprovalStep.Should().Be(2);
        (await db.PayrollApprovalHistories.CountAsync(h => h.PayrollRunId == runId && h.Action == PayrollApprovalAction.Approved))
            .Should().Be(1);
    }

    [Fact]
    public async Task Approve_MultiStep_DistinctApprovers_ReachesApproved()
    {
        var secondApprover = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 2, instanceId: instanceId);

        (await Service(_financeUserId).ApproveAsync(runId, null, null)).IsSuccess.Should().BeTrue();

        var second = await Service(secondApprover).ApproveAsync(runId, null, null);
        second.IsSuccess.Should().BeTrue();
        second.Value!.Status.Should().Be(nameof(PayrollRunStatus.Approved));
    }

    // ── BUG-076: per-step role binding (FR-2) ──────────────────────────────────

    [Fact]
    public async Task Approve_RolePerStep_EnforcesAssignedRole_AndDistinctPerson()
    {
        var submitter = Guid.NewGuid();
        var hrApprover = Guid.NewGuid();
        var otherHr = Guid.NewGuid();  // holds the HR role (step 1) but NOT the Finance role (step 2).
        var finApprover = Guid.NewGuid();

        var roleHr = await SeedApproverRoleAsync("HR Approver", hrApprover, otherHr);
        var roleFin = await SeedApproverRoleAsync("Finance Approver", finApprover);
        await SeedStepConfigAsync((1, roleHr), (2, roleFin));

        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: submitter,
            step: 1, totalSteps: 2, instanceId: Guid.NewGuid());

        // Step 1: an HR-role holder approves -> advances to step 2.
        var s1 = await Service(hrApprover).ApproveAsync(runId, null, null);
        s1.IsSuccess.Should().BeTrue();
        s1.Value!.CurrentApprovalStep.Should().Be(2);

        // Step 2 by a user WITHOUT the Finance role (and who did not approve step 1) -> 403 not_step_approver.
        var wrong = await Service(otherHr).ApproveAsync(runId, null, null);
        wrong.IsFailure.Should().BeTrue();
        wrong.StatusCode.Should().Be(403);
        wrong.ErrorCode.Should().Be("not_step_approver");

        // Step 2 by the Finance-role holder (distinct person) -> Approved.
        var s2 = await Service(finApprover).ApproveAsync(runId, null, null);
        s2.IsSuccess.Should().BeTrue();
        s2.Value!.Status.Should().Be(nameof(PayrollRunStatus.Approved));
    }

    // ── BUG-076: config is authoritative for the step count (FR-2) ─────────────

    [Fact]
    public async Task Submit_WithStepConfig_SetsTotalStepsFromConfig_IgnoringCallerValue()
    {
        var role1 = await SeedApproverRoleAsync("R1");
        var role2 = await SeedApproverRoleAsync("R2");
        await SeedStepConfigAsync((1, role1), (2, role2));
        var runId = await SeedRunAsync(PayrollRunStatus.ReviewPending);

        // Caller passes 1, but the 2-step config wins.
        var result = await Service(_hrUserId).SubmitForApprovalAsync(runId, totalApprovalSteps: 1, null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalApprovalSteps.Should().Be(2);

        using var db = Db();
        var run = await db.PayrollRuns.SingleAsync(r => r.Id == runId);
        run.TotalApprovalSteps.Should().Be(2);
    }

    // ── BUG-076: step-config CRUD (FR-2) ───────────────────────────────────────

    [Fact]
    public async Task SetStepConfig_Persists_AndAudits()
    {
        var role1 = await SeedApproverRoleAsync("HR Manager Role");
        var role2 = await SeedApproverRoleAsync("Finance Role");

        // Use the REAL audit logger bound to the SAME context so a persisted audit_log row is asserted.
        using var db = Db();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(_hrUserId);
        var audit = new PayrollAuditLogger(db, _tenantContext, currentUser, Substitute.For<ILogger<PayrollAuditLogger>>());
        var service = new PayrollApprovalService(db, _tenantContext, currentUser,
            Substitute.For<IPayrollNotificationService>(), audit, Substitute.For<ILogger<PayrollApprovalService>>());

        var result = await service.SetApprovalStepConfigAsync(new[]
        {
            new PayrollApprovalStepConfigItem { StepNumber = 1, RoleId = role1 },
            new PayrollApprovalStepConfigItem { StepNumber = 2, RoleId = role2 },
        }, "1.1.1.1");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(s => s.StepNumber).Should().ContainInOrder(1, 2);
        result.Value[0].RoleName.Should().Be("HR Manager Role");
        result.Value[1].RoleName.Should().Be("Finance Role");

        using var db2 = Db();
        (await db2.PayrollApprovalStepConfigs.CountAsync()).Should().Be(2);
        (await db2.AuditLogs.CountAsync(a => a.Action == "PayrollApprovalStepConfig.Updated")).Should().Be(1);
    }

    [Fact]
    public async Task SetStepConfig_ReplacesExistingConfig()
    {
        var role1 = await SeedApproverRoleAsync("R1");
        var role2 = await SeedApproverRoleAsync("R2");
        await SeedStepConfigAsync((1, role1), (2, role2));

        // Replace the 2-step config with a single step.
        var result = await Service(_hrUserId).SetApprovalStepConfigAsync(
            new[] { new PayrollApprovalStepConfigItem { StepNumber = 1, RoleId = role2 } }, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);

        using var db = Db();
        (await db.PayrollApprovalStepConfigs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SetStepConfig_NonContiguous_Returns400_AndPersistsNothing()
    {
        var r1 = await SeedApproverRoleAsync("R1");
        var r3 = await SeedApproverRoleAsync("R3");

        var result = await Service(_hrUserId).SetApprovalStepConfigAsync(new[]
        {
            new PayrollApprovalStepConfigItem { StepNumber = 1, RoleId = r1 },
            new PayrollApprovalStepConfigItem { StepNumber = 3, RoleId = r3 }, // gap: no step 2
        }, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("steps_not_contiguous");

        using var db = Db();
        (await db.PayrollApprovalStepConfigs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SetStepConfig_RoleLacksApprovePermission_Returns400()
    {
        // A role that exists in the tenant but does NOT hold Payroll.Approve.
        Guid roleNoApprove = Guid.NewGuid();
        using (var db = Db())
        {
            db.Roles.Add(new Role { Id = roleNoApprove, TenantId = _tenantId, Name = "No Approve" });
            await db.SaveChangesAsync();
        }

        var result = await Service(_hrUserId).SetApprovalStepConfigAsync(
            new[] { new PayrollApprovalStepConfigItem { StepNumber = 1, RoleId = roleNoApprove } }, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("role_missing_approve_permission");
    }

    [Fact]
    public async Task SetStepConfig_UnknownRole_Returns400()
    {
        var result = await Service(_hrUserId).SetApprovalStepConfigAsync(
            new[] { new PayrollApprovalStepConfigItem { StepNumber = 1, RoleId = Guid.NewGuid() } }, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("role_not_found");
    }

    // ── Re-submit after rejection starts a new instance (BR-3) ─────────────────

    [Fact]
    public async Task Resubmit_AfterRejection_StartsNewWorkflowInstance()
    {
        var originalInstance = Guid.NewGuid();
        var runId = await SeedRunAsync(PayrollRunStatus.Rejected, submittedBy: _hrUserId,
            instanceId: originalInstance);

        var result = await Service(_hrUserId).SubmitForApprovalAsync(runId, null, null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(PayrollRunStatus.AwaitingApproval));
        result.Value.WorkflowInstanceId.Should().NotBe(originalInstance);
        result.Value.WorkflowInstanceId.Should().NotBeNull();
    }
}
