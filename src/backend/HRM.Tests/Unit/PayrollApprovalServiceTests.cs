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
        int? step = null, int? totalSteps = null, Guid? instanceId = null,
        int payMonth = 5, int payYear = 2026)
    {
        using var db = Db();
        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = _tenantId, PayMonth = payMonth, PayYear = payYear,
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

    // ══════════════════════════════════════════════════════════════
    //  DF-14 — "pending approvals for the current approver" queue.
    //  The queue must mirror ApproveAsync's eligibility exactly: a run appears iff approving it would NOT 403.
    // ══════════════════════════════════════════════════════════════

    // ── Step-role: the queue INCLUDES a run whose current step role the caller holds. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_IncludesRun_WhenCallerHoldsCurrentStepRole()
    {
        var roleX = await SeedApproverRoleAsync("Finance", _financeUserId);
        await SeedStepConfigAsync((1, roleX));
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId, step: 1, totalSteps: 1);

        var result = await Service(_financeUserId).GetPendingApprovalsAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Select(r => r.RunId).Should().Contain(runId);
    }

    // ── Step-role EXCLUSION (the crux): a Payroll.Approve holder who does NOT hold the step's role is excluded. ──
    // Kills a mutant that ignores the per-step role gate and lists every AwaitingApproval run to every approver.
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_ExcludesRun_WhenCallerLacksCurrentStepRole()
    {
        var stepRole = await SeedApproverRoleAsync("Finance", Guid.NewGuid());   // held by SOMEONE ELSE
        await SeedApproverRoleAsync("HRApprover", _financeUserId);               // caller holds a DIFFERENT approve role
        await SeedStepConfigAsync((1, stepRole));
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId, step: 1, totalSteps: 1);

        var result = await Service(_financeUserId).GetPendingApprovalsAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Select(r => r.RunId).Should().NotContain(runId);
    }

    // ── Maker-checker: the caller's OWN submission is excluded when the tenant has >=2 eligible approvers. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_ExcludesOwnSubmission_WhenTwoOrMoreEligibleApprovers()
    {
        var roleX = await SeedApproverRoleAsync("Finance", _financeUserId, Guid.NewGuid()); // 2 eligible approvers
        await SeedStepConfigAsync((1, roleX));
        // The caller (_financeUserId) submitted this run themselves.
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _financeUserId, step: 1, totalSteps: 1);

        var result = await Service(_financeUserId).GetPendingApprovalsAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Select(r => r.RunId).Should().NotContain(runId);
    }

    // ── Maker-checker small-team exception: with <2 eligible approvers, the caller's own submission IS listed. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_IncludesOwnSubmission_WhenSmallTeam()
    {
        var roleX = await SeedApproverRoleAsync("Finance", _financeUserId); // ONLY the caller is eligible (<2)
        await SeedStepConfigAsync((1, roleX));
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _financeUserId, step: 1, totalSteps: 1);

        var result = await Service(_financeUserId).GetPendingApprovalsAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Select(r => r.RunId).Should().Contain(runId);
    }

    // ── Distinct-approver: a multi-step run the caller already approved (earlier step) is excluded from their queue. ──
    // Uses the REAL ApproveAsync to record the Approved history row (shared HasCallerAlreadyApprovedAsync predicate).
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_ExcludesRun_CallerAlreadyApprovedEarlierStep()
    {
        var instanceId = Guid.NewGuid();
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 2, instanceId: instanceId);

        // Caller approves step 1 → run advances to step 2, still AwaitingApproval, caller now has an Approved row.
        (await Service(_financeUserId).ApproveAsync(runId, null, null)).IsSuccess.Should().BeTrue();

        var result = await Service(_financeUserId).GetPendingApprovalsAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Select(r => r.RunId).Should().NotContain(runId); // they can't approve step 2 as well
    }

    // ── Only AwaitingApproval runs surface; ReviewPending / Approved are not a pending-approval action. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_OnlyAwaitingApproval_ExcludesOtherStatuses()
    {
        var awaiting = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId, step: 1, totalSteps: 1);
        var review = await SeedRunAsync(PayrollRunStatus.ReviewPending, submittedBy: _hrUserId);
        var approved = await SeedRunAsync(PayrollRunStatus.Approved, submittedBy: _hrUserId);

        var ids = (await Service(_financeUserId).GetPendingApprovalsAsync()).Value!.Select(r => r.RunId).ToList();

        ids.Should().Contain(awaiting);
        ids.Should().NotContain(review);
        ids.Should().NotContain(approved);
    }

    // ── The submitter display name is resolved from the tenant Employee linked to the submitter user (batched). ──
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_ResolvesSubmitterName_FromLinkedEmployee()
    {
        var submitter = Guid.NewGuid();
        using (var db = Db())
        {
            db.Employees.Add(new Employee
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, UserId = submitter,
                EmployeeNo = "EMP-9001", FirstName = "Ada", LastName = "Lovelace",
                Email = "ada@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
            });
            await db.SaveChangesAsync();
        }
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: submitter, step: 1, totalSteps: 1);

        var row = (await Service(_financeUserId).GetPendingApprovalsAsync()).Value!.Single(r => r.RunId == runId);

        row.SubmittedBy.Should().Be(submitter);
        row.InitiatedByName.Should().Be("Ada Lovelace");
    }

    // ── Isolation (BUG-003 class): a run in another tenant is invisible to this tenant's approver. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_IsTenantScoped_ForeignRunInvisible()
    {
        // Seed an AwaitingApproval run under a DIFFERENT tenant context, same physical store.
        var otherTenantId = Guid.NewGuid();
        var otherTenant = Substitute.For<ITenantContext>();
        otherTenant.TenantId.Returns(otherTenantId);
        otherTenant.IsResolved.Returns(true);
        var foreignRunId = BaseEntity.NewUuidV7();
        using (var db = TestDbContextFactory.Create(otherTenant, _dbName))
        {
            db.PayrollRuns.Add(new PayrollRun
            {
                Id = foreignRunId, TenantId = otherTenantId, PayMonth = 5, PayYear = 2026,
                Status = PayrollRunStatus.AwaitingApproval, InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
                TotalEmployees = 1, ProcessedEmployees = 1, TotalGross = 10m, TotalNet = 10m,
                SubmittedBy = Guid.NewGuid(), CurrentApprovalStep = 1, TotalApprovalSteps = 1,
            });
            await db.SaveChangesAsync();
        }

        var result = await Service(_financeUserId).GetPendingApprovalsAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Select(r => r.RunId).Should().NotContain(foreignRunId);
    }

    // ── Ordering: the queue returns runs newest period first (PayYear desc, then PayMonth desc). ──
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_OrdersNewestPeriodFirst()
    {
        // Seeded out of order across two years + two months so both order keys matter.
        var mar2026 = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId, payMonth: 3, payYear: 2026);
        var jul2026 = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId, payMonth: 7, payYear: 2026);
        var dec2025 = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId, payMonth: 12, payYear: 2025);

        var ids = (await Service(_financeUserId).GetPendingApprovalsAsync()).Value!.Select(r => r.RunId).ToList();

        // 2026-07, then 2026-03, then 2025-12.
        ids.Should().ContainInOrder(jul2026, mar2026, dec2025);
    }

    // ── Name resolution: a submitter with NO linked Employee falls back to the global User display name. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_ResolvesSubmitterName_FallsBackToUserDisplayName()
    {
        var submitter = Guid.NewGuid();
        using (var db = Db())
        {
            db.Users.Add(new User { Id = submitter, Email = "sam@acme.com", DisplayName = "Sam Submitter" });
            await db.SaveChangesAsync();
        }
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: submitter, step: 1, totalSteps: 1);

        var row = (await Service(_financeUserId).GetPendingApprovalsAsync()).Value!.Single(r => r.RunId == runId);

        row.InitiatedByName.Should().Be("Sam Submitter"); // no Employee row → User branch
    }

    // ── Name resolution precedence: the tenant Employee name WINS over a differently-named global User. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_ResolvesSubmitterName_PrefersEmployeeOverUser()
    {
        var submitter = Guid.NewGuid();
        using (var db = Db())
        {
            db.Users.Add(new User { Id = submitter, Email = "x@acme.com", DisplayName = "Global Name" });
            db.Employees.Add(new Employee
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, UserId = submitter,
                EmployeeNo = "EMP-9002", FirstName = "Tenant", LastName = "Person",
                Email = "tenant@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
            });
            await db.SaveChangesAsync();
        }
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: submitter, step: 1, totalSteps: 1);

        var row = (await Service(_financeUserId).GetPendingApprovalsAsync()).Value!.Single(r => r.RunId == runId);

        row.InitiatedByName.Should().Be("Tenant Person"); // Employee wins, not "Global Name"
    }

    // ── Distinct-approver positive control: a multi-step run the caller has NOT approved IS shown to them. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_IncludesMultiStepRun_CallerHasNotYetApproved()
    {
        var otherApprover = Guid.NewGuid();
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 2, instanceId: Guid.NewGuid());

        // A DIFFERENT approver clears step 1 → run advances to step 2, still AwaitingApproval.
        (await Service(otherApprover).ApproveAsync(runId, null, null)).IsSuccess.Should().BeTrue();

        // The caller has not approved any step → the run IS in their queue (contrast with the exclude arm).
        var ids = (await Service(_financeUserId).GetPendingApprovalsAsync()).Value!.Select(r => r.RunId).ToList();
        ids.Should().Contain(runId);
    }

    // ══════════════════════════════════════════════════════════════
    //  ISSUE-173 FR-3 — SLA auto-escalation: submit/advance stamps SlaDueAt from the step's SlaHours.
    //  (The escalator itself uses ExecuteUpdate CAS → proven on real Postgres, not InMemory.)
    // ══════════════════════════════════════════════════════════════

    private PayrollApprovalService Service(Guid actingUserId, TimeProvider clock)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(actingUserId);
        return new PayrollApprovalService(Db(), _tenantContext, currentUser,
            Substitute.For<IPayrollNotificationService>(), Substitute.For<IPayrollAuditLogger>(),
            Substitute.For<ILogger<PayrollApprovalService>>(), clock);
    }

    // ── A step with SlaHours set → submit stamps SlaDueAt = now + SlaHours (from the injected clock). ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task Submit_WithStepSla_StampsSlaDueAt_FromInjectedClock()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var roleId = await SeedApproverRoleAsync("Finance", _financeUserId);
        using (var db = Db())
        {
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StepNumber = 1, RoleId = roleId, SlaHours = 24,
            });
            await db.SaveChangesAsync();
        }
        var runId = await SeedRunAsync(PayrollRunStatus.ReviewPending);

        var result = await Service(_hrUserId, new FakeTimeProvider(now)).SubmitForApprovalAsync(runId, null, "ready", "1.2.3.4");
        result.IsSuccess.Should().BeTrue(result.Error);

        using var db2 = Db();
        var run = await db2.PayrollRuns.FirstAsync(r => r.Id == runId);
        run.SlaDueAt.Should().Be(now.UtcDateTime.AddHours(24));   // now + step-1 SlaHours
        run.EscalatedAt.Should().BeNull();                        // fresh step, not yet escalated
    }

    // ── No SlaHours on the step (opt-in) → SlaDueAt stays null → the run never escalates. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task Submit_NoStepSla_LeavesSlaDueAtNull()
    {
        var runId = await SeedRunAsync(PayrollRunStatus.ReviewPending); // no step config / no SlaHours

        (await Service(_hrUserId).SubmitForApprovalAsync(runId, null, "ready", "1.2.3.4")).IsSuccess.Should().BeTrue();

        using var db = Db();
        (await db.PayrollRuns.FirstAsync(r => r.Id == runId)).SlaDueAt.Should().BeNull();
    }

    // ── Advancing to the next step re-stamps SlaDueAt from THAT step's SlaHours and clears EscalatedAt. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task Approve_AdvancingToNextStep_ReStampsSlaDueAt_FromNextStepSla_AndClearsEscalatedAt()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var roleB = await SeedApproverRoleAsync("Step2");
        using (var db = Db())
        {
            // Only step 2 has an SLA (48h). Step 1 has no config → no step-role gate on the first approval.
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StepNumber = 2, RoleId = roleB, SlaHours = 48,
            });
            await db.SaveChangesAsync();
        }
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 2, instanceId: Guid.NewGuid());
        using (var db = Db())
        {
            var r = await db.PayrollRuns.FirstAsync(x => x.Id == runId);
            r.EscalatedAt = now.UtcDateTime.AddHours(-1); // pretend step 1 had been escalated
            await db.SaveChangesAsync();
        }

        (await Service(_financeUserId, new FakeTimeProvider(now)).ApproveAsync(runId, null, null)).IsSuccess.Should().BeTrue();

        using var db2 = Db();
        var run = await db2.PayrollRuns.FirstAsync(r => r.Id == runId);
        run.CurrentApprovalStep.Should().Be(2);
        run.SlaDueAt.Should().Be(now.UtcDateTime.AddHours(48)); // re-stamped from step-2 SlaHours
        run.EscalatedAt.Should().BeNull();                      // cleared for the fresh step
    }

    // ── Step-config validation: SlaHours must be > 0 when set. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task SetStepConfig_ZeroSlaHours_Returns400()
    {
        var r1 = await SeedApproverRoleAsync("R1");

        var result = await Service(_hrUserId).SetApprovalStepConfigAsync(
            new[] { new PayrollApprovalStepConfigItem { StepNumber = 1, RoleId = r1, SlaHours = 0 } }, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("sla_hours_invalid");
    }

    // ── Step-config validation: a BackupRoleId must be a real in-tenant role that holds Payroll.Approve. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task SetStepConfig_BackupRoleLacksApprovePermission_Returns400()
    {
        var r1 = await SeedApproverRoleAsync("R1");
        Guid backupNoApprove = Guid.NewGuid();
        using (var db = Db())
        {
            db.Roles.Add(new Role { Id = backupNoApprove, TenantId = _tenantId, Name = "Backup No Approve" });
            await db.SaveChangesAsync();
        }

        var result = await Service(_hrUserId).SetApprovalStepConfigAsync(
            new[] { new PayrollApprovalStepConfigItem { StepNumber = 1, RoleId = r1, BackupRoleId = backupNoApprove } }, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("role_missing_approve_permission");
    }

    // ══ ISSUE-173 FR-6 — approval delegation: primary approver on approved leave → the delegate is recorded. ══

    /// <summary>Seeds an employee linked to <paramref name="userId"/> with a leave request over [start, end] in the given status.</summary>
    private async Task SeedUserLeaveAsync(Guid userId, DateOnly start, DateOnly end, LeaveRequestStatus status)
    {
        using var db = Db();
        var empId = Guid.NewGuid();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = _tenantId, UserId = userId, EmployeeNo = $"EMP-{userId.ToString()[..4]}",
            FirstName = "On", LastName = "Leave", Email = $"{userId:N}@acme.com",
            Status = EmployeeStatus.Active, IsActive = true,
        });
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = empId, LeaveTypeId = Guid.NewGuid(),
            Status = status, StartDate = start, EndDate = end, TotalDays = (end.DayNumber - start.DayNumber + 1),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds an ACTIVE tenant user in a role that does NOT grant Payroll.Approve.</summary>
    private async Task SeedActiveUserWithoutApproveAsync(Guid userId)
    {
        using var db = Db();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = _tenantId, Name = "Viewer" });
        var utId = Guid.NewGuid();
        db.UserTenants.Add(new UserTenant { Id = utId, UserId = userId, TenantId = _tenantId, Status = UserTenantStatus.Active });
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    // ── Primary approver on leave at submit → the run is delegated to the delegate + a Delegated history row. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task Submit_PrimaryApproverOnLeave_DelegatesToDelegate_WritesDelegatedHistory()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var primaryUser = Guid.NewGuid();
        var delegateUser = Guid.NewGuid();
        var roleId = await SeedApproverRoleAsync("Finance", primaryUser, delegateUser);
        using (var db = Db())
        {
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StepNumber = 1, RoleId = roleId,
                PrimaryApproverUserId = primaryUser, DelegateUserId = delegateUser,
            });
            await db.SaveChangesAsync();
        }
        await SeedUserLeaveAsync(primaryUser, today.AddDays(-3), today.AddDays(3), LeaveRequestStatus.Approved);
        var runId = await SeedRunAsync(PayrollRunStatus.ReviewPending);

        (await Service(_hrUserId, new FakeTimeProvider(now)).SubmitForApprovalAsync(runId, null, "ready", "1.2.3.4"))
            .IsSuccess.Should().BeTrue();

        using var db2 = Db();
        var run = await db2.PayrollRuns.FirstAsync(r => r.Id == runId);
        run.DelegatedToUserId.Should().Be(delegateUser);
        (await db2.PayrollApprovalHistories.CountAsync(
            h => h.PayrollRunId == runId && h.Action == PayrollApprovalAction.Delegated)).Should().Be(1);
    }

    // ── Delegation ALSO fires at step-ADVANCE: approving step 1 activates step 2, whose primary is on leave. ──
    // Kills a mutant that drops the ApplyDelegationAsync call on the approve→next-step branch.
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task Approve_AdvancingToStepWithDelegatedPrimaryOnLeave_DelegatesAtThatStep()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var step2Primary = Guid.NewGuid();
        var step2Delegate = Guid.NewGuid();
        var roleB = await SeedApproverRoleAsync("Step2", step2Primary, step2Delegate);
        using (var db = Db())
        {
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StepNumber = 2, RoleId = roleB,
                PrimaryApproverUserId = step2Primary, DelegateUserId = step2Delegate,
            });
            await db.SaveChangesAsync();
        }
        await SeedUserLeaveAsync(step2Primary, today.AddDays(-1), today.AddDays(1), LeaveRequestStatus.Approved);
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 2, instanceId: Guid.NewGuid());

        (await Service(_financeUserId, new FakeTimeProvider(now)).ApproveAsync(runId, null, null)).IsSuccess.Should().BeTrue();

        using var db2 = Db();
        var run = await db2.PayrollRuns.FirstAsync(r => r.Id == runId);
        run.CurrentApprovalStep.Should().Be(2);
        run.DelegatedToUserId.Should().Be(step2Delegate);           // delegated at the newly-activated step 2
        (await db2.PayrollApprovalHistories.CountAsync(
            h => h.PayrollRunId == runId && h.Action == PayrollApprovalAction.Delegated)).Should().Be(1);
    }

    // ── Leave-DATE boundary: a leave that ended YESTERDAY does not span today → no delegation. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task Submit_PrimaryLeaveEndedYesterday_NoDelegation()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var primaryUser = Guid.NewGuid();
        var delegateUser = Guid.NewGuid();
        var roleId = await SeedApproverRoleAsync("Finance", primaryUser, delegateUser);
        using (var db = Db())
        {
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StepNumber = 1, RoleId = roleId,
                PrimaryApproverUserId = primaryUser, DelegateUserId = delegateUser,
            });
            await db.SaveChangesAsync();
        }
        await SeedUserLeaveAsync(primaryUser, today.AddDays(-5), today.AddDays(-1), LeaveRequestStatus.Approved); // ended yesterday
        var runId = await SeedRunAsync(PayrollRunStatus.ReviewPending);

        (await Service(_hrUserId, new FakeTimeProvider(now)).SubmitForApprovalAsync(runId, null, "ready", "1.2.3.4"))
            .IsSuccess.Should().BeTrue();

        (await Db().PayrollRuns.FirstAsync(r => r.Id == runId)).DelegatedToUserId.Should().BeNull();
    }

    // ── Leave-STATUS gate: a non-approved (Rejected) leave spanning today does not delegate. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task Submit_PrimaryNonApprovedLeaveSpanningToday_NoDelegation()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var primaryUser = Guid.NewGuid();
        var delegateUser = Guid.NewGuid();
        var roleId = await SeedApproverRoleAsync("Finance", primaryUser, delegateUser);
        using (var db = Db())
        {
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StepNumber = 1, RoleId = roleId,
                PrimaryApproverUserId = primaryUser, DelegateUserId = delegateUser,
            });
            await db.SaveChangesAsync();
        }
        // Spans today but is NOT approved → the Status == Approved gate excludes it.
        await SeedUserLeaveAsync(primaryUser, today.AddDays(-3), today.AddDays(3), LeaveRequestStatus.Rejected);
        var runId = await SeedRunAsync(PayrollRunStatus.ReviewPending);

        (await Service(_hrUserId, new FakeTimeProvider(now)).SubmitForApprovalAsync(runId, null, "ready", "1.2.3.4"))
            .IsSuccess.Should().BeTrue();

        (await Db().PayrollRuns.FirstAsync(r => r.Id == runId)).DelegatedToUserId.Should().BeNull();
    }

    // ── Validation: a delegate user who is not an active tenant user → 400 delegation_user_not_found. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task SetStepConfig_DelegateUserNotFound_Returns400()
    {
        var primaryUser = Guid.NewGuid();
        var r1 = await SeedApproverRoleAsync("R1", primaryUser); // primary is a valid approve-holding user

        var result = await Service(_hrUserId).SetApprovalStepConfigAsync(
            new[] { new PayrollApprovalStepConfigItem { StepNumber = 1, RoleId = r1, PrimaryApproverUserId = primaryUser, DelegateUserId = Guid.NewGuid() } }, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("delegation_user_not_found");
    }

    // ── Validation: a delegate user who does not hold Payroll.Approve → 400 delegation_user_missing_approve_permission. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task SetStepConfig_DelegateUserLacksApprove_Returns400()
    {
        var primaryUser = Guid.NewGuid();
        var delegateUser = Guid.NewGuid();
        var r1 = await SeedApproverRoleAsync("R1", primaryUser);
        await SeedActiveUserWithoutApproveAsync(delegateUser); // active tenant user, but no Payroll.Approve

        var result = await Service(_hrUserId).SetApprovalStepConfigAsync(
            new[] { new PayrollApprovalStepConfigItem { StepNumber = 1, RoleId = r1, PrimaryApproverUserId = primaryUser, DelegateUserId = delegateUser } }, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("delegation_user_missing_approve_permission");
    }

    // ── Primary approver NOT on leave → no delegation (DelegatedToUserId stays null). ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task Submit_PrimaryApproverNotOnLeave_NoDelegation()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var primaryUser = Guid.NewGuid();
        var delegateUser = Guid.NewGuid();
        var roleId = await SeedApproverRoleAsync("Finance", primaryUser, delegateUser);
        using (var db = Db())
        {
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StepNumber = 1, RoleId = roleId,
                PrimaryApproverUserId = primaryUser, DelegateUserId = delegateUser,
            });
            await db.SaveChangesAsync();
        }
        // No leave seeded for the primary → not on leave.
        var runId = await SeedRunAsync(PayrollRunStatus.ReviewPending);

        (await Service(_hrUserId, new FakeTimeProvider(now)).SubmitForApprovalAsync(runId, null, "ready", "1.2.3.4"))
            .IsSuccess.Should().BeTrue();

        using var db2 = Db();
        var run = await db2.PayrollRuns.FirstAsync(r => r.Id == runId);
        run.DelegatedToUserId.Should().BeNull();
        (await db2.PayrollApprovalHistories.CountAsync(
            h => h.PayrollRunId == runId && h.Action == PayrollApprovalAction.Delegated)).Should().Be(0);
    }

    // ── Step-config validation: setting only ONE of primary/delegate → 400 delegation_config_incomplete. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task SetStepConfig_IncompleteDelegation_Returns400()
    {
        var r1 = await SeedApproverRoleAsync("R1");

        var result = await Service(_hrUserId).SetApprovalStepConfigAsync(
            new[] { new PayrollApprovalStepConfigItem { StepNumber = 1, RoleId = r1, PrimaryApproverUserId = Guid.NewGuid() } }, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("delegation_config_incomplete");
    }
}
