// ============================================================================
// US-ADM-011 (Phase 1): Approval-workflow RUNTIME engine — service unit tests.
//
// Covers instance creation on submit with version snapshot + first-step activation (AC-1/AC-7),
// condition-skip via the pure WorkflowEvaluator (AC-3), sequential advance + completion + reject
// (AC-2/BR-2), assigned-approver authorization (AC-10), the atomic onApproved side-effect callback
// (AC-2 rollback semantics), the no-definition legacy fallback (AC-11), the real in-flight count +
// delete guard (AC-8), and tenant isolation (AC-9). EF Core InMemory (concurrency/AC-12 is exercised
// on real Postgres in WorkflowRuntimeConcurrencyPostgresTests).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class WorkflowRuntimeServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    public WorkflowRuntimeServiceTests()
    {
        _tenantContext = MakeTenantContext(_tenantId);
    }

    private static ITenantContext MakeTenantContext(Guid tenantId)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    private static ICurrentUser User(Guid userId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        return u;
    }

    private AppDbContext Db(ITenantContext? ctx = null)
        => TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);

    private WorkflowRuntimeService Runtime(ICurrentUser user, ITenantContext? ctx = null)
        => new(Db(ctx), ctx ?? _tenantContext, user, NullLogger<WorkflowRuntimeService>.Instance);

    /// <summary>Seeds an Active Leave workflow definition with NamedUser steps. Returns (lineageId, definitionId).</summary>
    private (Guid LineageId, Guid DefinitionId) SeedActiveLeaveWorkflow(
        params (int Order, Guid ApproverUserId, string? Condition)[] steps)
    {
        using var db = Db();
        var defId = BaseEntity.NewUuidV7();
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Id = defId, TenantId = _tenantId, Name = "Leave Approval", EntityType = WorkflowEntityType.Leave,
            LineageId = defId, Version = 1, Status = WorkflowStatus.Active, IsActive = true,
        });
        foreach (var s in steps)
        {
            db.WorkflowSteps.Add(new WorkflowStep
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, WorkflowDefinitionId = defId,
                StepOrder = s.Order, ApproverType = WorkflowApproverType.NamedUser,
                ApproverIdentifier = s.ApproverUserId, SlaHours = 24, ConditionJson = s.Condition,
            });
        }
        db.SaveChanges();
        return (defId, defId);
    }

    private static Dictionary<string, object?> Days(decimal days)
        => new() { ["days_requested"] = days };

    // ── AC-1 / AC-7: instance creation on submit with version snapshot ─────────

    [Fact]
    public async Task CreateOnSubmit_WithActiveDefinition_CreatesInstance_SnapshotsVersion_FirstStepPending()
    {
        var approver = Guid.NewGuid();
        var (lineageId, defId) = SeedActiveLeaveWorkflow((1, approver, null));
        var entityId = Guid.NewGuid();

        var result = await Runtime(User(Guid.NewGuid()))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, entityId, null, Days(3));

        result.InstanceCreated.Should().BeTrue();
        result.InstanceId.Should().NotBeNull();

        using var db = Db();
        var instance = db.WorkflowInstances.Single(i => i.Id == result.InstanceId);
        instance.WorkflowDefinitionId.Should().Be(defId);       // bound to the SPECIFIC version row (BR-1)
        instance.LineageId.Should().Be(lineageId);
        instance.Version.Should().Be(1);
        instance.EntityId.Should().Be(entityId);
        instance.Status.Should().Be(WorkflowInstanceStatus.InProgress);
        instance.CurrentStepOrder.Should().Be(1);

        var steps = db.WorkflowStepInstances.Where(s => s.WorkflowInstanceId == instance.Id).ToList();
        steps.Should().ContainSingle();
        steps[0].Decision.Should().Be(WorkflowStepDecision.Pending);
        steps[0].AssignedApproverUserId.Should().Be(approver);
        steps[0].SlaDueAt.Should().NotBeNull();
    }

    // ── AC-11: no active definition → legacy fallback (no instance) ────────────

    [Fact]
    public async Task CreateOnSubmit_NoActiveDefinition_ReturnsLegacy()
    {
        // No workflow seeded for this tenant.
        var result = await Runtime(User(Guid.NewGuid()))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, Guid.NewGuid(), null, Days(3));

        result.InstanceCreated.Should().BeFalse();
        result.InstanceId.Should().BeNull();

        using var db = Db();
        db.WorkflowInstances.Should().BeEmpty();
    }

    // ── AC-3: a step whose condition is not met is skipped (materialized) ──────

    [Fact]
    public async Task CreateOnSubmit_ConditionUnmet_SkipsStep_NotTheActiveStep()
    {
        var approver1 = Guid.NewGuid();
        var approver2 = Guid.NewGuid();
        // Step 2 only applies when days_requested > 5. Submit a 3-day request → step 2 is skipped.
        SeedActiveLeaveWorkflow(
            (1, approver1, null),
            (2, approver2, "{\"field\":\"days_requested\",\"operator\":\">\",\"value\":5}"));

        var result = await Runtime(User(Guid.NewGuid()))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, Guid.NewGuid(), null, Days(3));

        using var db = Db();
        var instance = db.WorkflowInstances.Single(i => i.Id == result.InstanceId);
        instance.CurrentStepOrder.Should().Be(1);
        instance.RouteStepOrders.Should().Be("1");

        var steps = db.WorkflowStepInstances.Where(s => s.WorkflowInstanceId == instance.Id).ToList();
        steps.Single(s => s.StepOrder == 1).Decision.Should().Be(WorkflowStepDecision.Pending);
        steps.Single(s => s.StepOrder == 2).Decision.Should().Be(WorkflowStepDecision.Skipped);
    }

    // ── AC-2: approve the final step → instance Approved ───────────────────────

    [Fact]
    public async Task Decide_ApproveSingleStep_CompletesInstance_RunsApprovedCallback()
    {
        var approver = Guid.NewGuid();
        SeedActiveLeaveWorkflow((1, approver, null));
        var entityId = Guid.NewGuid();
        var created = await Runtime(User(Guid.NewGuid()))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, entityId, null, Days(3));

        var approvedCallbackRan = false;
        var decision = await Runtime(User(approver)).DecideAsync(
            WorkflowEntityType.Leave, entityId, WorkflowDecisionAction.Approve, "ok",
            onApproved: _ => { approvedCallbackRan = true; return Task.FromResult(Result.Success()); });

        decision.IsSuccess.Should().BeTrue(decision.Error);
        decision.Value!.Outcome.Should().Be(WorkflowDecisionOutcome.InstanceApproved);
        approvedCallbackRan.Should().BeTrue();

        using var db = Db();
        db.WorkflowInstances.Single(i => i.Id == created.InstanceId)
            .Status.Should().Be(WorkflowInstanceStatus.Approved);
        db.WorkflowStepInstances.Single(s => s.WorkflowInstanceId == created.InstanceId)
            .Decision.Should().Be(WorkflowStepDecision.Approved);
    }

    // ── AC-2: approve step 1 of 2 → advances to step 2 (still in progress) ─────

    [Fact]
    public async Task Decide_ApproveFirstOfTwo_AdvancesToNextStep()
    {
        var a1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();
        SeedActiveLeaveWorkflow((1, a1, null), (2, a2, null));
        var entityId = Guid.NewGuid();
        var created = await Runtime(User(Guid.NewGuid()))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, entityId, null, Days(3));

        var decision = await Runtime(User(a1))
            .DecideAsync(WorkflowEntityType.Leave, entityId, WorkflowDecisionAction.Approve, null);

        decision.IsSuccess.Should().BeTrue(decision.Error);
        decision.Value!.Outcome.Should().Be(WorkflowDecisionOutcome.StepAdvanced);
        decision.Value!.NextStepOrder.Should().Be(2);

        using var db = Db();
        db.WorkflowInstances.Single(i => i.Id == created.InstanceId)
            .Status.Should().Be(WorkflowInstanceStatus.InProgress);
        var step2 = db.WorkflowStepInstances.Single(s => s.WorkflowInstanceId == created.InstanceId && s.StepOrder == 2);
        step2.Decision.Should().Be(WorkflowStepDecision.Pending);
        step2.AssignedApproverUserId.Should().Be(a2);
    }

    // ── AC-2 / BR-2: reject terminates the instance ───────────────────────────

    [Fact]
    public async Task Decide_Reject_TerminatesInstance_RunsRejectedCallback()
    {
        var approver = Guid.NewGuid();
        SeedActiveLeaveWorkflow((1, approver, null), (2, Guid.NewGuid(), null));
        var entityId = Guid.NewGuid();
        var created = await Runtime(User(Guid.NewGuid()))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, entityId, null, Days(3));

        var rejectedCallbackRan = false;
        var decision = await Runtime(User(approver)).DecideAsync(
            WorkflowEntityType.Leave, entityId, WorkflowDecisionAction.Reject, "no",
            onRejected: _ => { rejectedCallbackRan = true; return Task.CompletedTask; });

        decision.Value!.Outcome.Should().Be(WorkflowDecisionOutcome.InstanceRejected);
        rejectedCallbackRan.Should().BeTrue();

        using var db = Db();
        db.WorkflowInstances.Single(i => i.Id == created.InstanceId)
            .Status.Should().Be(WorkflowInstanceStatus.Rejected);
    }

    // ── AC-10: a non-approver cannot decide (403, no transition) ───────────────

    [Fact]
    public async Task Decide_NonApprover_Returns403_NoTransition()
    {
        var approver = Guid.NewGuid();
        SeedActiveLeaveWorkflow((1, approver, null));
        var entityId = Guid.NewGuid();
        var created = await Runtime(User(Guid.NewGuid()))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, entityId, null, Days(3));

        var stranger = Guid.NewGuid();
        var decision = await Runtime(User(stranger))
            .DecideAsync(WorkflowEntityType.Leave, entityId, WorkflowDecisionAction.Approve, null);

        decision.IsFailure.Should().BeTrue();
        decision.StatusCode.Should().Be(403);
        decision.ErrorCode.Should().Be("not_step_approver");

        using var db = Db();
        db.WorkflowInstances.Single(i => i.Id == created.InstanceId)
            .Status.Should().Be(WorkflowInstanceStatus.InProgress);
        db.WorkflowStepInstances.Single(s => s.WorkflowInstanceId == created.InstanceId)
            .Decision.Should().Be(WorkflowStepDecision.Pending);
    }

    // ── AC-2: a failing onApproved side-effect aborts the completion ───────────

    [Fact]
    public async Task Decide_ApprovedCallbackFails_DoesNotComplete()
    {
        var approver = Guid.NewGuid();
        SeedActiveLeaveWorkflow((1, approver, null));
        var entityId = Guid.NewGuid();
        var created = await Runtime(User(Guid.NewGuid()))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, entityId, null, Days(3));

        var decision = await Runtime(User(approver)).DecideAsync(
            WorkflowEntityType.Leave, entityId, WorkflowDecisionAction.Approve, null,
            onApproved: _ => Task.FromResult(Result.Failure("Insufficient balance.", 400)));

        decision.IsFailure.Should().BeTrue();
        decision.StatusCode.Should().Be(400);

        // On InMemory (no transaction) the failure returns before SaveChanges, so the store is unchanged.
        using var db = Db();
        db.WorkflowInstances.Single(i => i.Id == created.InstanceId)
            .Status.Should().Be(WorkflowInstanceStatus.InProgress);
        db.WorkflowStepInstances.Single(s => s.WorkflowInstanceId == created.InstanceId)
            .Decision.Should().Be(WorkflowStepDecision.Pending);
    }

    // ── AC-8 / FR-10: real in-flight count + delete guard ─────────────────────

    [Fact]
    public async Task InFlightInstance_BlocksDefinitionDelete_409()
    {
        var approver = Guid.NewGuid();
        var (lineageId, defId) = SeedActiveLeaveWorkflow((1, approver, null));
        // Seed the owning tenant row so WorkflowService can resolve the plan limit path.
        using (var seed = Db())
        {
            seed.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
            seed.SaveChanges();
        }

        await Runtime(User(Guid.NewGuid()))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, Guid.NewGuid(), null, Days(3));

        (await Runtime(User(Guid.NewGuid())).CountInFlightAsync(lineageId)).Should().Be(1);

        var currentUser = User(Guid.NewGuid());
        var svc = new WorkflowService(Db(), _tenantContext, currentUser, NullLogger<WorkflowService>.Instance);
        var delete = await svc.DeleteAsync(defId);

        delete.IsFailure.Should().BeTrue();
        delete.StatusCode.Should().Be(409);
        delete.ErrorCode.Should().Be("workflow_in_flight");
    }

    // ── AC-9: instances are tenant-isolated ───────────────────────────────────

    [Fact]
    public async Task Instances_AreTenantIsolated()
    {
        var approver = Guid.NewGuid();
        SeedActiveLeaveWorkflow((1, approver, null));
        var entityId = Guid.NewGuid();
        var created = await Runtime(User(Guid.NewGuid()))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, entityId, null, Days(3));
        created.InstanceCreated.Should().BeTrue();

        // A different tenant sees no instances and cannot decide the other tenant's request.
        var otherTenant = MakeTenantContext(Guid.NewGuid());
        using (var otherDb = Db(otherTenant))
            otherDb.WorkflowInstances.Should().BeEmpty();

        var decision = await Runtime(User(approver), otherTenant)
            .DecideAsync(WorkflowEntityType.Leave, entityId, WorkflowDecisionAction.Approve, null);
        decision.IsFailure.Should().BeTrue();
        decision.ErrorCode.Should().Be("workflow_not_in_flight");
    }
}
