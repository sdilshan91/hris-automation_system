using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
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

    public WorkflowRuntimeService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<WorkflowRuntimeService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
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
            .Include(w => w.Steps)
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
        if (!_db.Database.IsRelational())
            return await DecideCoreAsync(entityType, entityId, action, comment, onApproved, onRejected, rowLock: false, cancellationToken);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            var result = await DecideCoreAsync(entityType, entityId, action, comment, onApproved, onRejected, rowLock: true, cancellationToken);
            if (result.IsSuccess)
                await tx.CommitAsync(cancellationToken);
            else
                await tx.RollbackAsync(cancellationToken);
            return result;
        });
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
        var instance = await _db.WorkflowInstances
            .FirstOrDefaultAsync(
                i => i.EntityType == entityType && i.EntityId == entityId
                     && i.Status == WorkflowInstanceStatus.InProgress,
                cancellationToken);

        if (instance is null)
            return Result<WorkflowDecisionResult>.Failure(
                "No in-flight workflow instance for this request.", 409, "workflow_not_in_flight");

        // Load the active step-instance (the Pending step at the current order). On Postgres, take a pessimistic
        // row lock first so a concurrent decision / SLA job blocks here and re-reads the committed decision.
        if (rowLock)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM workflow_step_instances WHERE workflow_instance_id = {instance.Id} AND step_order = {instance.CurrentStepOrder} FOR UPDATE",
                cancellationToken);
        }

        var step = await _db.WorkflowStepInstances
            .FirstOrDefaultAsync(
                s => s.WorkflowInstanceId == instance.Id && s.StepOrder == instance.CurrentStepOrder,
                cancellationToken);

        if (step is null)
            return Result<WorkflowDecisionResult>.Failure("No active step for this instance.", 409, "workflow_no_active_step");

        if (rowLock)
            await _db.Entry(step).ReloadAsync(cancellationToken); // authoritative decision committed under the lock

        // AC-12: a concurrent decision (or SLA job) already actioned this step — the loser is idempotently rejected.
        if (step.Decision != WorkflowStepDecision.Pending)
            return Result<WorkflowDecisionResult>.Failure(
                "This step has already been actioned.", 409, "step_already_decided");

        // AC-10: only the resolved approver (or a holder of the role) of the Pending step may decide it.
        if (!await IsAuthorizedApproverAsync(step, cancellationToken))
            return Result<WorkflowDecisionResult>.Failure(
                "You are not the assigned approver for the current step.", 403, "not_step_approver");

        var now = DateTime.UtcNow;
        var actingUserId = _currentUser.UserId;

        if (action == WorkflowDecisionAction.Reject)
        {
            step.Decision = WorkflowStepDecision.Rejected;
            step.DecidedByUserId = actingUserId;
            step.DecidedAt = now;
            step.Comments = comment;

            instance.Status = WorkflowInstanceStatus.Rejected;
            instance.CompletedAt = now;

            if (onRejected is not null)
                await onRejected(cancellationToken);

            AddAudit("workflow.instance.rejected", instance, $"Step {step.StepOrder} rejected by {actingUserId}.");
            await _db.SaveChangesAsync(cancellationToken);

            return Result<WorkflowDecisionResult>.Success(
                new WorkflowDecisionResult(instance.Id, WorkflowDecisionOutcome.InstanceRejected, null));
        }

        // Approve the current step.
        step.Decision = WorkflowStepDecision.Approved;
        step.DecidedByUserId = actingUserId;
        step.DecidedAt = now;
        step.Comments = comment;

        // AC-2/AC-3: advance to the next applicable step (skips were materialized at creation) or complete.
        var orderedSteps = await LoadDefinitionStepsAsync(instance.WorkflowDefinitionId, cancellationToken);
        var applicableOrders = ParseRoute(instance.RouteStepOrders);

        var activated = await ActivateNextAsync(
            instance, orderedSteps, applicableOrders, fromOrderExclusive: step.StepOrder, cancellationToken);

        if (activated)
        {
            AddAudit("workflow.step.advanced", instance, $"Step {step.StepOrder} approved by {actingUserId}; advanced to step {instance.CurrentStepOrder}.");
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

        AddAudit("workflow.instance.approved", instance, $"Final step {step.StepOrder} approved by {actingUserId}; instance approved.");
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
            var assigned = await ResolveApproverAsync(step, instance, cancellationToken);
            _db.WorkflowStepInstances.Add(new WorkflowStepInstance
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = instance.TenantId,
                WorkflowInstanceId = instance.Id,
                StepOrder = step.StepOrder,
                IsParallel = step.IsParallel,
                ApproverType = step.ApproverType,
                ApproverIdentifier = step.ApproverIdentifier,
                AssignedApproverUserId = assigned,
                Decision = WorkflowStepDecision.Pending,
                SlaDueAt = step.SlaHours > 0 ? now.AddHours(step.SlaHours) : null,
                CreatedAt = now,
            });
            instance.CurrentStepOrder = step.StepOrder;
            return true;
        }

        return false;
    }

    /// <summary>
    /// FR-5: resolves a step's approver TYPE to a concrete user id at activation. LineManager → the requester's
    /// reporting manager's user; DepartmentHead → the requester's department manager's user; NamedUser → the
    /// identifier directly; Role → null (any holder of the role may decide — authorization checks membership).
    /// </summary>
    private async Task<Guid?> ResolveApproverAsync(
        WorkflowStep step, WorkflowInstance instance, CancellationToken cancellationToken)
    {
        switch (step.ApproverType)
        {
            case WorkflowApproverType.NamedUser:
                return step.ApproverIdentifier;

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
}
