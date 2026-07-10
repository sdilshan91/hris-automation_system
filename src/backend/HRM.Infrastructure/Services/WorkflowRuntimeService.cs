using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Workflows;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-011 (Phase 1) approval-workflow RUNTIME. Instantiates a workflow per submitted request, snapshots the
/// definition version (BR-1/AC-7), routes SEQUENTIALLY through the configured steps reusing the pure
/// <see cref="WorkflowEvaluator"/> for condition-skip (AC-3), and applies approver decisions with a
/// single-winner transactional advance (AC-12/NFR-4). Runs in the normal resolved-tenant context — isolation
/// (AC-9/BR-7) is the EF global query filter on the two runtime tables (RLS-eligible).
///
/// <para>Domain-agnostic by design: the terminal domain side-effect (e.g. the leave ledger deduction) is
/// supplied by the caller as an <c>onApproved</c>/<c>onRejected</c> callback and executed INSIDE this service's
/// transaction, so the workflow state and the domain change commit atomically without a DI cycle back into the
/// domain service (see <see cref="IWorkflowRuntime"/>).</para>
///
/// <para>Phase 1 covers sequential steps only. Parallel-group join/short-circuit (AC-4) and the SLA-escalation
/// job (AC-5) are US-ADM-011b; delegation (AC-6) and the remaining entity wiring are US-ADM-011c. The
/// <see cref="WorkflowStepInstance.SlaDueAt"/>/<see cref="WorkflowStepInstance.EscalatedAt"/> columns are
/// populated here so 011b can build on them.</para>
/// </summary>
public sealed class WorkflowRuntimeService : IWorkflowRuntime
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<WorkflowRuntimeService> _logger;

    // US-ADM-011b (FR-13): notification intents accumulated during a decision/creation transaction and FLUSHED
    // only AFTER commit (a retry of the execution strategy would otherwise double-send). Optional dispatcher so
    // the unit/Postgres harnesses that construct the service directly need no notification wiring.
    private readonly INotificationDispatcher? _dispatcher;
    private readonly List<(Guid UserId, string EventKey, WorkflowInstance Instance, int StepOrder, string? Decision)> _pending = new();

    public WorkflowRuntimeService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<WorkflowRuntimeService> logger,
        INotificationDispatcher? dispatcher = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
        _dispatcher = dispatcher;
    }

    // ── Create on submit (AC-1/AC-7/AC-11) ────────────────────────────────────

    public async Task<WorkflowInstanceCreationResult> CreateInstanceOnSubmitAsync(
        WorkflowEntityType entityType,
        Guid entityId,
        Guid? requesterEmployeeId,
        IReadOnlyDictionary<string, object?> requestData,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return WorkflowInstanceCreationResult.Legacy();

        // AC-11: find the tenant's ACTIVE definition for this entity type. None → legacy single-level path.
        var definition = await _db.WorkflowDefinitions
            .Include(w => w.Steps).ThenInclude(s => s.Approvers)
            .FirstOrDefaultAsync(
                w => w.EntityType == entityType && w.Status == WorkflowStatus.Active, cancellationToken);

        if (definition is null)
        {
            _logger.LogInformation(
                "No active {EntityType} workflow definition for tenant {TenantId}; entity {EntityId} uses the legacy path (AC-11).",
                entityType, _tenantContext.TenantId, entityId);
            return WorkflowInstanceCreationResult.Legacy();
        }

        // AC-3: reuse the pure evaluator to compute the applicable (non-skipped) route.
        var applicable = WorkflowEvaluator.Evaluate(definition.Steps, requestData);
        var applicableOrders = applicable.Select(a => a.StepOrder).ToHashSet();

        if (applicableOrders.Count == 0)
        {
            // Every step was condition-skipped for this request — no approval is required; fall back to legacy
            // rather than auto-approving with no approver (keeps Phase 1 simple; rare in practice).
            _logger.LogInformation(
                "Active {EntityType} workflow for tenant {TenantId} skipped every step for entity {EntityId}; using the legacy path.",
                entityType, _tenantContext.TenantId, entityId);
            return WorkflowInstanceCreationResult.Legacy();
        }

        var tenantId = _tenantContext.TenantId;
        var instance = new WorkflowInstance
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            WorkflowDefinitionId = definition.Id,
            LineageId = definition.LineageId,
            Version = definition.Version,
            EntityType = entityType,
            EntityId = entityId,
            Status = WorkflowInstanceStatus.InProgress,
            CurrentStepOrder = 0,
            RouteStepOrders = string.Join(",", applicableOrders.OrderBy(o => o)),
            RequesterUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            RequesterEmployeeId = requesterEmployeeId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.WorkflowInstances.Add(instance);

        // §13 Q4: materialize the FULL skip chain up front (every condition-unmet step as a Skipped row) so the
        // chain/history is complete immediately, then activate the first applicable step.
        var orderedSteps = definition.Steps.OrderBy(s => s.StepOrder).ToList();
        MaterializeSkippedSteps(instance, orderedSteps, applicableOrders);
        await ActivateNextAsync(instance, orderedSteps, applicableOrders, fromOrderExclusive: int.MinValue, cancellationToken);

        AddAudit("workflow.instance.created", instance, $"Instance created for {entityType} {entityId} on workflow v{instance.Version}.");
        await _db.SaveChangesAsync(cancellationToken);

        // FR-13: the first step's assignment notifications were queued during activation — flush AFTER commit.
        await FlushNotificationsAsync(cancellationToken);

        _logger.LogInformation(
            "Workflow instance {InstanceId} created (v{Version}) for {EntityType} {EntityId}, first step {StepOrder}, tenant {TenantId}.",
            instance.Id, instance.Version, entityType, entityId, instance.CurrentStepOrder, tenantId);

        return WorkflowInstanceCreationResult.Created(instance.Id);
    }

    // ── Decide (AC-2/AC-3/AC-10/AC-12) ─────────────────────────────────────────

    public async Task<Result<WorkflowDecisionResult>> DecideAsync(
        WorkflowEntityType entityType,
        Guid entityId,
        WorkflowDecisionAction action,
        string? comment,
        Func<CancellationToken, Task<Result>>? onApproved = null,
        Func<CancellationToken, Task>? onRejected = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<WorkflowDecisionResult>.Failure("No tenant context.", 400);

        // AC-12/NFR-4: the state transition (row-locked, atomic) runs inside the retry-safe execution strategy
        // on a relational provider so concurrent decisions serialize on the step-instance row lock and exactly
        // one wins. On InMemory (unit tests) transactions/raw-SQL are unsupported, so it runs directly.
        Result<WorkflowDecisionResult> result;
        if (!_db.Database.IsRelational())
        {
            result = await DecideCoreAsync(entityType, entityId, action, comment, onApproved, onRejected, rowLock: false, cancellationToken);
        }
        else
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            result = await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
                var r = await DecideCoreAsync(entityType, entityId, action, comment, onApproved, onRejected, rowLock: true, cancellationToken);
                if (r.IsSuccess)
                    await tx.CommitAsync(cancellationToken);
                else
                    await tx.RollbackAsync(cancellationToken);
                return r;
            });
        }

        // FR-13: dispatch the queued assignment/decision notifications ONLY after a successful commit (a retry of
        // the execution strategy re-runs DecideCoreAsync, which clears _pending at the top — so only the winning
        // attempt's intents survive). Never let a notification failure fail the decision.
        if (result.IsSuccess)
            await FlushNotificationsAsync(cancellationToken);
        else
            _pending.Clear();

        return result;
    }

    private async Task<Result<WorkflowDecisionResult>> DecideCoreAsync(
        WorkflowEntityType entityType,
        Guid entityId,
        WorkflowDecisionAction action,
        string? comment,
        Func<CancellationToken, Task<Result>>? onApproved,
        Func<CancellationToken, Task>? onRejected,
        bool rowLock,
        CancellationToken cancellationToken)
    {
        // The execution strategy may re-invoke this on a transient retry — drop any intents queued by a prior
        // (rolled-back) attempt so only the winning attempt's notifications flush (FR-13).
        _pending.Clear();

        var instance = await _db.WorkflowInstances
            .FirstOrDefaultAsync(
                i => i.EntityType == entityType && i.EntityId == entityId
                     && i.Status == WorkflowInstanceStatus.InProgress,
                cancellationToken);

        if (instance is null)
            return Result<WorkflowDecisionResult>.Failure(
                "No in-flight workflow instance for this request.", 409, "workflow_not_in_flight");

        // Load the active step-instance GROUP (all rows at the current order — for a parallel step this is the
        // whole co-approver group; for a sequential step it is a single row). On Postgres, take a pessimistic
        // row lock over the group first so a concurrent decision / SLA job blocks and re-reads committed state.
        if (rowLock)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM workflow_step_instances WHERE workflow_instance_id = {instance.Id} AND step_order = {instance.CurrentStepOrder} FOR UPDATE",
                cancellationToken);
        }

        var group = await _db.WorkflowStepInstances
            .Where(s => s.WorkflowInstanceId == instance.Id && s.StepOrder == instance.CurrentStepOrder)
            .ToListAsync(cancellationToken);

        if (group.Count == 0)
            return Result<WorkflowDecisionResult>.Failure("No active step for this instance.", 409, "workflow_no_active_step");

        if (rowLock)
            foreach (var s in group)
                await _db.Entry(s).ReloadAsync(cancellationToken); // authoritative decisions committed under the lock

        var now = DateTime.UtcNow;
        var actingUserId = _currentUser.UserId;

        // AC-12 / idempotency: this user already decided a row in this group → duplicate/loser.
        if (group.Any(s => s.DecidedByUserId == actingUserId && s.Decision != WorkflowStepDecision.Pending))
            return Result<WorkflowDecisionResult>.Failure(
                "This step has already been actioned.", 409, "step_already_decided");

        // AC-10: find the Pending row in the group this user is authorized to decide.
        WorkflowStepInstance? mine = null;
        foreach (var s in group.Where(s => s.Decision == WorkflowStepDecision.Pending))
        {
            if (await IsAuthorizedApproverAsync(s, cancellationToken))
            {
                mine = s;
                break;
            }
        }

        if (mine is null)
            return Result<WorkflowDecisionResult>.Failure(
                "You are not the assigned approver for the current step.", 403, "not_step_approver");

        if (action == WorkflowDecisionAction.Reject)
        {
            // AC-4 / BR-2: any rejection short-circuits the whole (parallel) group and the instance.
            mine.Decision = WorkflowStepDecision.Rejected;
            mine.DecidedByUserId = actingUserId;
            mine.DecidedAt = now;
            mine.Comments = comment;

            instance.Status = WorkflowInstanceStatus.Rejected;
            instance.CompletedAt = now;

            if (onRejected is not null)
                await onRejected(cancellationToken);

            AddAudit("workflow.instance.rejected", instance, $"Step {mine.StepOrder} rejected by {actingUserId}.");
            QueueDecisionNotification(instance, mine.StepOrder, rejected: true);
            await _db.SaveChangesAsync(cancellationToken);

            return Result<WorkflowDecisionResult>.Success(
                new WorkflowDecisionResult(instance.Id, WorkflowDecisionOutcome.InstanceRejected, null));
        }

        // Approve this approver's row.
        mine.Decision = WorkflowStepDecision.Approved;
        mine.DecidedByUserId = actingUserId;
        mine.DecidedAt = now;
        mine.Comments = comment;

        // AC-4: a parallel group only advances once EVERY approver row has approved. If co-approvers remain
        // Pending, the request stays put and we record the partial approval.
        var stillPending = group.Any(s => !ReferenceEquals(s, mine) && s.Decision == WorkflowStepDecision.Pending);
        if (stillPending)
        {
            AddAudit("workflow.step.recorded", instance, $"Step {mine.StepOrder} approved by {actingUserId}; awaiting co-approvers.");
            await _db.SaveChangesAsync(cancellationToken);
            return Result<WorkflowDecisionResult>.Success(
                new WorkflowDecisionResult(instance.Id, WorkflowDecisionOutcome.StepRecorded, instance.CurrentStepOrder));
        }

        // AC-2/AC-3: the group is complete — advance to the next applicable step (skips were materialized at
        // creation) or complete the instance.
        var orderedSteps = await LoadDefinitionStepsAsync(instance.WorkflowDefinitionId, cancellationToken);
        var applicableOrders = ParseRoute(instance.RouteStepOrders);

        var activated = await ActivateNextAsync(
            instance, orderedSteps, applicableOrders, fromOrderExclusive: mine.StepOrder, cancellationToken);

        if (activated)
        {
            AddAudit("workflow.step.advanced", instance, $"Step {mine.StepOrder} approved by {actingUserId}; advanced to step {instance.CurrentStepOrder}.");
            await _db.SaveChangesAsync(cancellationToken);
            return Result<WorkflowDecisionResult>.Success(
                new WorkflowDecisionResult(instance.Id, WorkflowDecisionOutcome.StepAdvanced, instance.CurrentStepOrder));
        }

        // No more applicable steps → instance approved. Run the domain side-effect inside this transaction; if it
        // fails (e.g. insufficient leave balance) the whole transition rolls back and the step stays Pending.
        if (onApproved is not null)
        {
            var sideEffect = await onApproved(cancellationToken);
            if (sideEffect.IsFailure)
                return Result<WorkflowDecisionResult>.Failure(
                    sideEffect.Error ?? "The approval could not be completed.", sideEffect.StatusCode ?? 400, sideEffect.ErrorCode);
        }

        instance.Status = WorkflowInstanceStatus.Approved;
        instance.CompletedAt = now;

        AddAudit("workflow.instance.approved", instance, $"Final step {mine.StepOrder} approved by {actingUserId}; instance approved.");
        QueueDecisionNotification(instance, mine.StepOrder, rejected: false);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<WorkflowDecisionResult>.Success(
            new WorkflowDecisionResult(instance.Id, WorkflowDecisionOutcome.InstanceApproved, null));
    }

    // ── In-flight count (AC-8/FR-10) ───────────────────────────────────────────

    public Task<int> CountInFlightAsync(Guid lineageId, CancellationToken cancellationToken = default)
        => _db.WorkflowInstances
            .CountAsync(i => i.LineageId == lineageId && i.Status == WorkflowInstanceStatus.InProgress, cancellationToken);

    public async Task<WorkflowInstanceEntityRef?> GetInstanceEntityAsync(
        Guid instanceId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return null;

        return await _db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Id == instanceId)
            .Select(i => new WorkflowInstanceEntityRef(i.EntityType, i.EntityId, i.Status))
            .FirstOrDefaultAsync(cancellationToken);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// §13 Q4: materializes a <see cref="WorkflowStepDecision.Skipped"/> row for every condition-unmet step so
    /// the chain/history is complete immediately at instance creation. Called once, before the first activation.
    /// </summary>
    private void MaterializeSkippedSteps(
        WorkflowInstance instance,
        IReadOnlyList<WorkflowStep> orderedSteps,
        IReadOnlySet<int> applicableOrders)
    {
        foreach (var step in orderedSteps)
        {
            if (applicableOrders.Contains(step.StepOrder))
                continue;

            _db.WorkflowStepInstances.Add(new WorkflowStepInstance
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = instance.TenantId,
                WorkflowInstanceId = instance.Id,
                StepOrder = step.StepOrder,
                IsParallel = step.IsParallel,
                ApproverType = step.ApproverType,
                ApproverIdentifier = step.ApproverIdentifier,
                Decision = WorkflowStepDecision.Skipped,
                CreatedAt = DateTime.UtcNow,
            });
        }
    }

    /// <summary>
    /// Creates the next APPLICABLE step (the first with order &gt; <paramref name="fromOrderExclusive"/> that is in
    /// the evaluated route) as a <see cref="WorkflowStepDecision.Pending"/> row — resolving its approver + SLA due
    /// time — and sets <see cref="WorkflowInstance.CurrentStepOrder"/>. Skipped steps are already materialized
    /// (see <see cref="MaterializeSkippedSteps"/>). Returns false when no applicable step remains (the instance
    /// should complete).
    /// </summary>
    private async Task<bool> ActivateNextAsync(
        WorkflowInstance instance,
        IReadOnlyList<WorkflowStep> orderedSteps,
        IReadOnlySet<int> applicableOrders,
        int fromOrderExclusive,
        CancellationToken cancellationToken)
    {
        foreach (var step in orderedSteps)
        {
            if (step.StepOrder <= fromOrderExclusive || !applicableOrders.Contains(step.StepOrder))
                continue;

            var now = DateTime.UtcNow;

            // AC-4: a parallel step fans out ONE Pending step-instance per DISTINCT resolved approver (the step's
            // own primary approver plus its additional Approvers children). A sequential step keeps its single
            // row. The SLA clock starts at activation time (Q2). Dedupe resolved users; for unresolved rows
            // (Role/LineManager-with-no-manager) dedupe by (type, identifier).
            var specs = new List<(WorkflowApproverType Type, Guid? Id)> { (step.ApproverType, step.ApproverIdentifier) };
            if (step.IsParallel)
                specs.AddRange(step.Approvers.Select(a => (a.ApproverType, a.ApproverIdentifier)));

            var seenUsers = new HashSet<Guid>();
            var seenUnresolved = new HashSet<(WorkflowApproverType, Guid?)>();
            for (int si = 0; si < specs.Count; si++)
            {
                var (type, id) = specs[si];
                var assigned = await ResolveApproverSpecAsync(type, id, instance, cancellationToken);

                // US-ADM-011c (AC-6/FR-9, Q3 snapshot-at-activation): if delegation is configured on the step and
                // the step's PRIMARY approver (si == 0) is on an approved leave spanning today, route this row to
                // the backup (recorded as a reassignment + audit + notification — NOT by flipping Decision, which
                // stays Pending/actionable; WorkflowStepDecision.Delegated stays reserved/unused). Delegation is a
                // step-level config applied only to the primary approver of BOTH sequential and parallel steps;
                // per-child (parallel co-approver) delegation is a documented Phase-3 simplification. Evaluated
                // once at activation only — never re-evaluated later (Q3).
                if (si == 0 && step.DelegationEnabled && assigned is Guid primaryUser
                    && await HasActiveApprovedLeaveAsync(primaryUser, cancellationToken))
                {
                    if (step.DelegationBackupUserId is { } backup)
                    {
                        assigned = backup;
                        QueueDelegationNotification(instance, step.StepOrder, backup);
                        AddAudit("workflow.step.delegated", instance,
                            $"Step {step.StepOrder} primary approver on leave; delegated to backup {backup}.");
                    }
                    else
                    {
                        // AC-6: no backup configured → keep the primary approver, notify the Tenant Admin.
                        await QueueTenantAdminDelegationNotificationAsync(instance, step.StepOrder, cancellationToken);
                        AddAudit("workflow.step.delegated", instance,
                            $"Step {step.StepOrder} primary approver on leave but no backup configured; Tenant Admin notified.");
                    }
                }

                if (assigned is Guid u)
                {
                    if (!seenUsers.Add(u))
                        continue;
                }
                else if (!seenUnresolved.Add((type, id)))
                {
                    continue;
                }

                _db.WorkflowStepInstances.Add(new WorkflowStepInstance
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = instance.TenantId,
                    WorkflowInstanceId = instance.Id,
                    StepOrder = step.StepOrder,
                    IsParallel = step.IsParallel,
                    ApproverType = type,
                    ApproverIdentifier = id,
                    AssignedApproverUserId = assigned,
                    Decision = WorkflowStepDecision.Pending,
                    SlaDueAt = step.SlaHours > 0 ? now.AddHours(step.SlaHours) : null,
                    CreatedAt = now,
                });

                if (assigned is Guid au)
                    QueueAssignmentNotification(instance, step.StepOrder, au); // FR-13
            }

            instance.CurrentStepOrder = step.StepOrder;
            return true; // an applicable step existed → activated (even if all approvers were unresolvable Role rows)
        }

        return false;
    }

    /// <summary>
    /// FR-5: resolves a step's approver TYPE to a concrete user id at activation. LineManager → the requester's
    /// reporting manager's user; DepartmentHead → the requester's department manager's user; NamedUser → the
    /// identifier directly; Role → null (any holder of the role may decide — authorization checks membership).
    /// </summary>
    private Task<Guid?> ResolveApproverAsync(
        WorkflowStep step, WorkflowInstance instance, CancellationToken cancellationToken)
        => ResolveApproverSpecAsync(step.ApproverType, step.ApproverIdentifier, instance, cancellationToken);

    /// <summary>
    /// FR-5: resolves an approver (type, identifier) spec to a concrete user id. Shared by the sequential path and
    /// the US-ADM-011b parallel fan-out (which resolves each co-approver spec independently). LineManager → the
    /// requester's reporting manager's user; DepartmentHead → the requester's department manager's user; NamedUser
    /// → the identifier directly; Role → null (any holder of the role may decide — authorization checks membership).
    /// </summary>
    private async Task<Guid?> ResolveApproverSpecAsync(
        WorkflowApproverType type, Guid? identifier, WorkflowInstance instance, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case WorkflowApproverType.NamedUser:
                return identifier;

            case WorkflowApproverType.LineManager:
            {
                if (instance.RequesterEmployeeId is not { } empId)
                    return null;
                var requester = await _db.Employees.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == empId, cancellationToken);
                if (requester?.ReportsToEmployeeId is not { } managerId)
                    return null;
                return await _db.Employees.AsNoTracking()
                    .Where(e => e.Id == managerId).Select(e => e.UserId).FirstOrDefaultAsync(cancellationToken);
            }

            case WorkflowApproverType.DepartmentHead:
            {
                if (instance.RequesterEmployeeId is not { } empId)
                    return null;
                var requester = await _db.Employees.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == empId, cancellationToken);
                if (requester is null)
                    return null;
                var headEmployeeId = await _db.Departments.AsNoTracking()
                    .Where(d => d.Id == requester.DepartmentId).Select(d => d.ManagerId).FirstOrDefaultAsync(cancellationToken);
                if (headEmployeeId is not { } headId)
                    return null;
                return await _db.Employees.AsNoTracking()
                    .Where(e => e.Id == headId).Select(e => e.UserId).FirstOrDefaultAsync(cancellationToken);
            }

            case WorkflowApproverType.Role:
            default:
                return null; // Role: unresolved to a single user — authorization checks role membership instead.
        }
    }

    /// <summary>
    /// AC-10: the current user is authorized to decide the step iff they are the resolved assigned approver, or
    /// (for a Role step) they hold the step's role in the current tenant.
    /// </summary>
    private async Task<bool> IsAuthorizedApproverAsync(WorkflowStepInstance step, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
            return false;

        if (step.AssignedApproverUserId is { } assigned)
            return assigned == userId;

        if (step.ApproverType == WorkflowApproverType.Role && step.ApproverIdentifier is { } roleId)
        {
            var tenantId = _tenantContext.TenantId;
            return await _db.UserTenantRoles
                .IgnoreQueryFilters()
                .AnyAsync(
                    utr => utr.RoleId == roleId
                           && utr.UserTenant.UserId == userId
                           && utr.UserTenant.TenantId == tenantId,
                    cancellationToken);
        }

        return false;
    }

    private async Task<IReadOnlyList<WorkflowStep>> LoadDefinitionStepsAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken)
        => await _db.WorkflowSteps.AsNoTracking()
            .Include(s => s.Approvers)
            .Where(s => s.WorkflowDefinitionId == workflowDefinitionId)
            .OrderBy(s => s.StepOrder)
            .ToListAsync(cancellationToken);

    private static IReadOnlySet<int> ParseRoute(string routeStepOrders)
        => string.IsNullOrWhiteSpace(routeStepOrders)
            ? new HashSet<int>()
            : routeStepOrders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .ToHashSet();

    /// <summary>NFR-5: append a tenant audit_log row for a runtime transition.</summary>
    private void AddAudit(string eventType, WorkflowInstance instance, string detail)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = instance.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = eventType,
            Action = eventType,
            ResourceType = "WorkflowInstance",
            ResourceId = instance.Id.ToString(),
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    // ── FR-13 notifications (US-NTF-006) ───────────────────────────────────────

    /// <summary>
    /// US-ADM-011c (AC-6/FR-9): true when <paramref name="userId"/> maps to an employee who has an APPROVED leave
    /// request spanning today — the trigger for step delegation at activation. Guard: a user with no employee row
    /// (e.g. a NamedUser approver who is not an employee) returns false. Mirrors the overlap query in
    /// <see cref="LeaveRequestService"/>.
    /// </summary>
    private async Task<bool> HasActiveApprovedLeaveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var empId = await _db.Employees.AsNoTracking()
            .Where(e => e.UserId == userId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(cancellationToken);
        if (empId is not { } eid)
            return false;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await _db.LeaveRequests.AsNoTracking().AnyAsync(
            lr => lr.EmployeeId == eid && lr.Status == LeaveRequestStatus.Approved
                  && lr.StartDate <= today && lr.EndDate >= today,
            cancellationToken);
    }

    /// <summary>Queue a "step delegated" notification for the backup approver a delegated step routed to (FR-9).</summary>
    private void QueueDelegationNotification(WorkflowInstance instance, int stepOrder, Guid backupUserId)
    {
        if (backupUserId == Guid.Empty)
            return;
        _pending.Add((backupUserId, "workflow_step_delegated", instance, stepOrder, null));
    }

    /// <summary>
    /// AC-6: when a delegated step's primary approver is on leave but NO backup is configured, notify the tenant
    /// admins (holders of <c>Tenant.ManageWorkflows</c>). Resolves them like <see cref="WorkflowSlaEscalator"/>
    /// and enqueues one intent per admin (flushed after commit).
    /// </summary>
    private async Task QueueTenantAdminDelegationNotificationAsync(
        WorkflowInstance instance, int stepOrder, CancellationToken cancellationToken)
    {
        var adminUserIds = await (
                from ut in _db.UserTenants.IgnoreQueryFilters()
                where ut.TenantId == instance.TenantId && ut.Status == UserTenantStatus.Active
                join utr in _db.UserTenantRoles.IgnoreQueryFilters() on ut.Id equals utr.UserTenantId
                join rp in _db.RolePermissions.IgnoreQueryFilters() on utr.RoleId equals rp.RoleId
                where rp.Permission == PermissionCatalog.Tenant.ManageWorkflows
                select ut.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var uid in adminUserIds)
            if (uid != Guid.Empty)
                _pending.Add((uid, "workflow_step_delegated", instance, stepOrder, null));
    }

    /// <summary>Queue an "approval step assigned" in-app+email notification for a newly-assigned approver.</summary>
    private void QueueAssignmentNotification(WorkflowInstance instance, int stepOrder, Guid approverUserId)
    {
        if (approverUserId == Guid.Empty)
            return;
        _pending.Add((approverUserId, "workflow_step_assigned", instance, stepOrder, null));
    }

    /// <summary>Queue an "approval decision" notification to the requester when the instance reaches a terminal state.</summary>
    private void QueueDecisionNotification(WorkflowInstance instance, int stepOrder, bool rejected)
    {
        if (instance.RequesterUserId is not { } requester || requester == Guid.Empty)
            return;
        _pending.Add((requester, "workflow_request_decided", instance, stepOrder, rejected ? "rejected" : "approved"));
    }

    /// <summary>
    /// FR-13: dispatch the queued notification intents AFTER commit. Each leg is wrapped in try/catch that logs and
    /// swallows — a notification failure must never fail the decision (mirrors the leave/payroll notification legs).
    /// No-ops when no dispatcher is wired (the direct-construction test harnesses).
    /// </summary>
    private async Task FlushNotificationsAsync(CancellationToken cancellationToken)
    {
        if (_dispatcher is null || _pending.Count == 0)
        {
            _pending.Clear();
            return;
        }

        var intents = _pending.ToList();
        _pending.Clear();

        foreach (var (userId, eventKey, instance, stepOrder, decision) in intents)
        {
            try
            {
                var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["workflow"] = new Dictionary<string, object?>
                    {
                        ["entityType"] = instance.EntityType.ToString(),
                        ["stepOrder"] = stepOrder,
                        ["requestId"] = instance.EntityId.ToString(),
                        ["decision"] = decision,
                    },
                });
                var request = new NotificationRequest(
                    TenantId: instance.TenantId,
                    EventKey: eventKey,
                    PayloadJson: payloadJson,
                    RecipientUserId: userId,
                    RecipientEmail: null,
                    NotificationType: null);

                await _dispatcher.SendInAppAsync(request, cancellationToken);
                await _dispatcher.SendEmailAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Workflow notification {EventKey} for user {UserId} (instance {InstanceId}) failed (non-fatal).",
                    eventKey, userId, instance.Id);
            }
        }
    }
}
