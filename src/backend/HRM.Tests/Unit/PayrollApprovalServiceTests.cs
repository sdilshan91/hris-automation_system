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
        var instanceId = Guid.NewGuid();
        var runId = await SeedRunAsync(PayrollRunStatus.AwaitingApproval, submittedBy: _hrUserId,
            step: 1, totalSteps: 2, instanceId: instanceId);

        // First approval — advances to step 2, still AwaitingApproval.
        var first = await Service(_financeUserId).ApproveAsync(runId, null, null);
        first.IsSuccess.Should().BeTrue();
        first.Value!.Status.Should().Be(nameof(PayrollRunStatus.AwaitingApproval));
        first.Value.CurrentApprovalStep.Should().Be(2);

        // Second approval — all steps complete, run becomes Approved.
        var second = await Service(_financeUserId).ApproveAsync(runId, null, null);
        second.IsSuccess.Should().BeTrue();
        second.Value!.Status.Should().Be(nameof(PayrollRunStatus.Approved));

        using var db = Db();
        var historyCount = await db.PayrollApprovalHistories.CountAsync(h => h.PayrollRunId == runId);
        historyCount.Should().Be(2); // one per approval step
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
