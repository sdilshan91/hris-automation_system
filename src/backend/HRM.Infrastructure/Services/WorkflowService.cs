using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Workflows.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Workflows;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-007: Tenant-Admin approval-workflow DEFINITION management. Runs in the NORMAL resolved-tenant
/// context — isolation (BR-7) is the EF global query filter on WorkflowDefinition/WorkflowStep (RLS deferred).
/// Each mutation writes a before/after audit row (NFR-4).
///
/// <para><b>Versioning (FR-3/AC-3):</b> editing an ACTIVE workflow creates a NEW row (Version+1) sharing the
/// same <see cref="WorkflowDefinition.LineageId"/>; the prior version is retained. A Draft is edited in place.</para>
///
/// <para><b>One-active-per-type (BR-2):</b> activating a workflow (on create or restore) first archives the
/// current active workflow for that entity type.</para>
///
/// <para><b>Plan limit (FR-4/AC-4):</b> the count of ACTIVE (non-archived) workflows is compared against
/// <see cref="Tenant.MaxWorkflows"/> (null = unlimited).</para>
///
/// <para><b>DEFERRED runtime engine:</b> live request routing through these definitions, per-request
/// WorkflowInstance/StepInstance creation, SLA Hangfire timers + escalation (BR-4), live delegation when an
/// approver is on leave (AC-5), and the Redis workflow cache (NFR-2) are NOT implemented here. The pure
/// condition/parallel EVALUATION lives in <see cref="WorkflowEvaluator"/>; only its invocation from live flows
/// is deferred. The BR-6 in-flight-instance guard currently always passes because no instance table exists.</para>
/// </summary>
public sealed class WorkflowService : IWorkflowService
{
    /// <summary>
    /// AC-4: the EXACT plan-limit message the story specifies. The <c>{limit}</c> placeholder is filled with the
    /// tenant's <see cref="Tenant.MaxWorkflows"/>.
    /// </summary>
    private const string PlanLimitMessageTemplate =
        "You have reached the maximum number of workflows ({0}) for your plan. Please upgrade or archive an existing workflow.";

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<WorkflowService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ── List (AC-1) ───────────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<WorkflowListItemDto>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<WorkflowListItemDto>>.Failure("No tenant context.", 400);

        var rows = await _db.WorkflowDefinitions
            .AsNoTracking()
            .Select(w => new
            {
                w.Id,
                w.LineageId,
                w.Name,
                w.EntityType,
                w.Version,
                w.Status,
                w.IsActive,
                w.IsDefault,
                StepCount = w.Steps.Count(),
                w.CreatedAt,
                w.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        // US-ADM-011c FR-10: the real count of live (InProgress) runtime instances per lineage — one grouped query.
        var inFlight = (await _db.WorkflowInstances
                .AsNoTracking()
                .Where(i => i.Status == WorkflowInstanceStatus.InProgress)
                .GroupBy(i => i.LineageId)
                .Select(g => new { LineageId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.LineageId, x => x.Count);

        var items = rows
            .OrderBy(w => w.EntityType)
            .ThenBy(w => w.Name)
            .ThenByDescending(w => w.Version)
            .Select(w => new WorkflowListItemDto(
                w.Id,
                w.LineageId,
                w.Name,
                w.EntityType.ToString(),
                w.Version,
                w.Status.ToString(),
                w.IsActive,
                w.IsDefault,
                w.StepCount,
                w.CreatedAt,
                w.UpdatedAt,
                inFlight.TryGetValue(w.LineageId, out var c) ? c : 0))
            .ToList();

        return Result<IReadOnlyList<WorkflowListItemDto>>.Success(items);
    }

    // ── Get ───────────────────────────────────────────────────────────────────

    public async Task<Result<WorkflowDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<WorkflowDetailDto>.Failure("No tenant context.", 400);

        var workflow = await _db.WorkflowDefinitions
            .AsNoTracking()
            .Include(w => w.Steps).ThenInclude(s => s.Approvers)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (workflow is null)
            return Result<WorkflowDetailDto>.Failure("Workflow not found.", 404);

        return Result<WorkflowDetailDto>.Success(ToDetailDto(workflow));
    }

    // ── Instance reads (US-ADM-011c FR-10/FR-12) ───────────────────────────────

    public async Task<Result<WorkflowInstanceDetailDto>> GetInstanceAsync(
        Guid instanceId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<WorkflowInstanceDetailDto>.Failure("No tenant context.", 400);

        var instance = await _db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.StepInstances)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance is null)
            return Result<WorkflowInstanceDetailDto>.Failure("Workflow instance not found.", 404, "workflow_instance_not_found");

        var steps = instance.StepInstances
            .OrderBy(s => s.StepOrder).ThenBy(s => s.Id)
            .Select(s => new WorkflowInstanceStepDto(
                s.StepOrder,
                s.IsParallel,
                s.ApproverType.ToString(),
                s.AssignedApproverUserId,
                s.Decision.ToString(),
                s.DecidedByUserId,
                s.DecidedAt,
                s.Comments,
                s.SlaDueAt,
                s.EscalatedAt))
            .ToList();

        return Result<WorkflowInstanceDetailDto>.Success(new WorkflowInstanceDetailDto(
            instance.Id,
            instance.EntityType.ToString(),
            instance.EntityId,
            instance.LineageId,
            instance.Version,
            instance.Status.ToString(),
            instance.CurrentStepOrder,
            instance.CreatedAt,
            instance.CompletedAt,
            steps));
    }

    public async Task<Result<PagedWorkflowInstancesDto>> ListInstancesAsync(
        Guid lineageId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PagedWorkflowInstancesDto>.Failure("No tenant context.", 400);

        // Mirror the leave-queue paging: clamp pageSize 1..50 (default 20); page is 1-based.
        int size = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 50);
        int pageNo = page <= 0 ? 1 : page;

        var baseQuery = _db.WorkflowInstances.AsNoTracking().Where(i => i.LineageId == lineageId);

        int totalCount = await baseQuery.CountAsync(cancellationToken);

        var items = await baseQuery
            .OrderByDescending(i => i.CreatedAt)
            .Skip((pageNo - 1) * size)
            .Take(size)
            .Select(i => new WorkflowInstanceListItemDto(
                i.Id,
                i.EntityId,
                i.Version,
                i.Status.ToString(),
                i.CurrentStepOrder,
                i.CreatedAt,
                i.CompletedAt))
            .ToListAsync(cancellationToken);

        return Result<PagedWorkflowInstancesDto>.Success(
            new PagedWorkflowInstancesDto(items, totalCount, pageNo, size));
    }

    // ── Create (AC-2/FR-1/FR-4/FR-5/BR-2) ─────────────────────────────────────

    public async Task<Result<WorkflowDetailDto>> CreateAsync(
        CreateWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<WorkflowDetailDto>.Failure("No tenant context.", 400);

        if (!Enum.TryParse<WorkflowEntityType>(request.EntityType, ignoreCase: true, out var entityType))
            return Result<WorkflowDetailDto>.Failure($"Invalid entity type '{request.EntityType}'.", 400);

        if (request.Steps is not { Count: > 0 })
            return Result<WorkflowDetailDto>.Failure("A workflow must have at least one step.", 400);

        var stepValidation = await ValidateStepsAsync(request.Steps, cancellationToken);
        if (stepValidation.IsFailure)
            return Result<WorkflowDetailDto>.Failure(stepValidation.Error!, stepValidation.StatusCode ?? 400);

        var activate = request.Activate;

        // FR-4/AC-4: enforce the plan limit on the count of ACTIVE (non-archived) workflows. Only a newly
        // ACTIVE workflow can push the tenant over the limit; a Draft does not count.
        if (activate)
        {
            var limitCheck = await EnsureWithinPlanLimitAsync(cancellationToken);
            if (limitCheck.IsFailure)
                return Result<WorkflowDetailDto>.Failure(
                    limitCheck.Error!, limitCheck.StatusCode ?? 400, limitCheck.ErrorCode);
        }

        var tenantId = _tenantContext.TenantId;

        // BR-2: activating archives the prior active workflow for the same entity type.
        if (activate)
            await ArchiveActiveForEntityTypeAsync(entityType, cancellationToken);

        var definitionId = BaseEntity.NewUuidV7();
        var definition = new WorkflowDefinition
        {
            Id = definitionId,
            TenantId = tenantId,
            Name = request.Name.Trim(),
            EntityType = entityType,
            LineageId = definitionId, // v1: lineage == own id.
            Version = 1,
            Status = activate ? WorkflowStatus.Active : WorkflowStatus.Draft,
            IsActive = activate,
            IsDefault = false,
            CreatedAt = DateTime.UtcNow,
            Steps = MapSteps(request.Steps, tenantId),
        };

        _db.WorkflowDefinitions.Add(definition);

        await PersistAsync(
            "workflow.created", before: null, after: ToAuditSnapshot(definition), definition.Id, cancellationToken);

        return Result<WorkflowDetailDto>.Success(ToDetailDto(definition));
    }

    // ── Update / new version (AC-3/FR-3) ──────────────────────────────────────

    public async Task<Result<WorkflowDetailDto>> UpdateAsync(
        Guid lineageId, UpdateWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<WorkflowDetailDto>.Failure("No tenant context.", 400);

        if (request.Steps is not { Count: > 0 })
            return Result<WorkflowDetailDto>.Failure("A workflow must have at least one step.", 400);

        // The latest version of the lineage (Draft or Active) is the one being edited. Load WITHOUT the steps
        // navigation — steps are managed directly via the DbSet so the parent's navigation never needs fixup
        // (which avoids an InMemory change-tracking conflict between a modified parent and replaced children).
        var current = await _db.WorkflowDefinitions
            .Where(w => w.LineageId == lineageId)
            .OrderByDescending(w => w.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
            return Result<WorkflowDetailDto>.Failure("Workflow not found.", 404);

        if (current.Status == WorkflowStatus.Archived)
            return Result<WorkflowDetailDto>.Failure(
                "An archived workflow cannot be edited. Restore it first.", 409, "workflow_archived");

        var stepValidation = await ValidateStepsAsync(request.Steps, cancellationToken);
        if (stepValidation.IsFailure)
            return Result<WorkflowDetailDto>.Failure(stepValidation.Error!, stepValidation.StatusCode ?? 400);

        var tenantId = _tenantContext.TenantId;
        var currentSteps = await _db.WorkflowSteps
            .Include(s => s.Approvers)
            .Where(s => s.WorkflowDefinitionId == current.Id)
            .ToListAsync(cancellationToken);
        var before = ToAuditSnapshot(current, currentSteps);

        WorkflowDefinition result;
        List<WorkflowStep> resultSteps;

        if (current.Status == WorkflowStatus.Active)
        {
            // FR-3/AC-3: editing an ACTIVE workflow creates a NEW VERSION; the prior version row (and its steps)
            // is retained. The prior version is no longer the live one (in-flight instances would still
            // reference it once the runtime engine exists). The new version becomes the active one.
            current.IsActive = false;
            current.Status = WorkflowStatus.Archived;
            current.UpdatedAt = DateTime.UtcNow;

            var newId = BaseEntity.NewUuidV7();
            result = new WorkflowDefinition
            {
                Id = newId,
                TenantId = tenantId,
                Name = request.Name.Trim(),
                EntityType = current.EntityType,
                LineageId = current.LineageId,
                Version = current.Version + 1,
                Status = WorkflowStatus.Active,
                IsActive = true,
                IsDefault = current.IsDefault,
                CreatedAt = DateTime.UtcNow,
            };
            _db.WorkflowDefinitions.Add(result);

            resultSteps = MapSteps(request.Steps, tenantId, result.Id);
            _db.WorkflowSteps.AddRange(resultSteps);
            // US-ADM-011b: insert the parallel-approver children explicitly (deterministic across InMemory +
            // Postgres — the parent steps are directly added, not attached via a definition navigation).
            _db.WorkflowStepApprovers.AddRange(resultSteps.SelectMany(s => s.Approvers));
        }
        else
        {
            // Draft: edit in place (no new version), replacing its steps via the DbSet (delete old, add new).
            // US-ADM-011b: remove the old steps' approver children first (InMemory has no DB cascade).
            _db.WorkflowStepApprovers.RemoveRange(currentSteps.SelectMany(s => s.Approvers));
            _db.WorkflowSteps.RemoveRange(currentSteps);

            current.Name = request.Name.Trim();
            current.UpdatedAt = DateTime.UtcNow;
            result = current;

            resultSteps = MapSteps(request.Steps, tenantId, current.Id);
            _db.WorkflowSteps.AddRange(resultSteps);
            _db.WorkflowStepApprovers.AddRange(resultSteps.SelectMany(s => s.Approvers));
        }

        await PersistAsync(
            "workflow.updated", before, ToAuditSnapshot(result, resultSteps), result.Id, cancellationToken);

        return Result<WorkflowDetailDto>.Success(ToDetailDto(result, resultSteps));
    }

    // ── Archive (FR-6) ────────────────────────────────────────────────────────

    public async Task<Result<WorkflowDetailDto>> ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<WorkflowDetailDto>.Failure("No tenant context.", 400);

        var workflow = await _db.WorkflowDefinitions
            .Include(w => w.Steps).ThenInclude(s => s.Approvers)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (workflow is null)
            return Result<WorkflowDetailDto>.Failure("Workflow not found.", 404);

        if (workflow.Status == WorkflowStatus.Archived)
            return Result<WorkflowDetailDto>.Success(ToDetailDto(workflow));

        var before = ToAuditSnapshot(workflow);
        workflow.IsActive = false;
        workflow.Status = WorkflowStatus.Archived;
        workflow.UpdatedAt = DateTime.UtcNow;

        await PersistAsync("workflow.archived", before, ToAuditSnapshot(workflow), workflow.Id, cancellationToken);

        return Result<WorkflowDetailDto>.Success(ToDetailDto(workflow));
    }

    // ── Restore (FR-6/BR-2) ───────────────────────────────────────────────────

    public async Task<Result<WorkflowDetailDto>> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<WorkflowDetailDto>.Failure("No tenant context.", 400);

        var workflow = await _db.WorkflowDefinitions
            .Include(w => w.Steps).ThenInclude(s => s.Approvers)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (workflow is null)
            return Result<WorkflowDetailDto>.Failure("Workflow not found.", 404);

        if (workflow.Status != WorkflowStatus.Archived)
            return Result<WorkflowDetailDto>.Failure("Only an archived workflow can be restored.", 409, "workflow_not_archived");

        // FR-4/AC-4: restoring re-activates the workflow, so re-check the plan limit.
        var limitCheck = await EnsureWithinPlanLimitAsync(cancellationToken);
        if (limitCheck.IsFailure)
            return Result<WorkflowDetailDto>.Failure(
                limitCheck.Error!, limitCheck.StatusCode ?? 400, limitCheck.ErrorCode);

        var before = ToAuditSnapshot(workflow);

        // BR-2 / BUG-006: archive any currently-active workflow for this entity type and PERSIST that archive
        // FIRST, before activating the restored one. The partial unique index
        // ix_workflow_definitions_tenant_entitytype_active — (tenant_id, entity_type) WHERE is_active = true —
        // is non-deferrable, so the archive (true→false) and the activate (false→true) must NOT land in the
        // same SaveChanges batch: EF does not guarantee it emits the archive UPDATE before the activate
        // UPDATE, so a single batch can momentarily leave two rows active for the type and Postgres rejects it
        // with 23505 (the reported 500). Splitting the saves guarantees the previous active is is_active=false
        // in the DB before the restored row is set is_active=true. (InMemory does not enforce the partial
        // index, which is why the unit suite never surfaced this.)
        await ArchiveActiveForEntityTypeAsync(workflow.EntityType, cancellationToken, excludeId: workflow.Id);
        await _db.SaveChangesAsync(cancellationToken);

        workflow.IsActive = true;
        workflow.Status = WorkflowStatus.Active;
        workflow.UpdatedAt = DateTime.UtcNow;

        await PersistAsync("workflow.restored", before, ToAuditSnapshot(workflow), workflow.Id, cancellationToken);

        return Result<WorkflowDetailDto>.Success(ToDetailDto(workflow));
    }

    // ── Delete (BR-6) ─────────────────────────────────────────────────────────

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("No tenant context.", 400);

        var workflow = await _db.WorkflowDefinitions
            .Include(w => w.Steps).ThenInclude(s => s.Approvers)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (workflow is null)
            return Result.Failure("Workflow not found.", 404);

        // BR-6/AC-8 (US-ADM-011): deletion is blocked while the definition lineage has any live (InProgress)
        // runtime instances. This is the REAL count over workflow_instances (tenant-scoped by the global query
        // filter), replacing the former hardcoded 0. Archiving remains allowed (handled by ArchiveAsync).
        var inFlightCount = await _db.WorkflowInstances
            .CountAsync(
                i => i.LineageId == workflow.LineageId && i.Status == WorkflowInstanceStatus.InProgress,
                cancellationToken);
        if (inFlightCount > 0)
            return Result.Failure(
                "This workflow has in-flight requests and cannot be deleted. Archive it instead.", 409, "workflow_in_flight");

        var before = ToAuditSnapshot(workflow);
        // US-ADM-011b: remove the parallel-approver children explicitly first (InMemory has no DB cascade).
        _db.WorkflowStepApprovers.RemoveRange(workflow.Steps.SelectMany(s => s.Approvers));
        _db.WorkflowSteps.RemoveRange(workflow.Steps);
        _db.WorkflowDefinitions.Remove(workflow);

        await PersistAsync("workflow.deleted", before, after: null, workflow.Id, cancellationToken);

        return Result.Success();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// FR-5: validates that every Role/NamedUser approver (and escalation/delegation reference) resolves to a
    /// real role/user WITHIN the current tenant. Role existence uses the tenant-filtered Roles set; user
    /// existence uses an active UserTenant membership; condition JSON is re-checked defensively. Returns the
    /// first failure (the FluentValidation layer has already covered the shape).
    /// </summary>
    private async Task<Result> ValidateStepsAsync(
        IReadOnlyList<WorkflowStepRequest> steps, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        // Pre-load the candidate role/user ids referenced so we hit the DB once.
        var roleIds = new HashSet<Guid>();
        var userIds = new HashSet<Guid>();

        foreach (var step in steps)
        {
            if (!Enum.TryParse<WorkflowApproverType>(step.ApproverType, ignoreCase: true, out var approverType))
                return Result.Failure($"Invalid approver type '{step.ApproverType}'.", 400);

            if (step.SlaHours <= 0)
                return Result.Failure("SLA hours must be a positive integer.", 400);

            var conditionError = WorkflowCondition.Validate(step.ConditionJson);
            if (conditionError is not null)
                return Result.Failure(conditionError, 400);

            CollectReference(approverType, step.ApproverIdentifier, roleIds, userIds);

            // US-ADM-011b: the additional parallel approvers (NamedUser ids) must also be tenant members.
            if (step.IsParallel && step.ParallelApproverIdentifiers is { Count: > 0 })
                foreach (var uid in step.ParallelApproverIdentifiers)
                    if (uid != Guid.Empty)
                        userIds.Add(uid);

            if (step.EscalationApproverType is not null)
            {
                if (!Enum.TryParse<WorkflowApproverType>(step.EscalationApproverType, ignoreCase: true, out var escType))
                    return Result.Failure($"Invalid escalation approver type '{step.EscalationApproverType}'.", 400);

                CollectReference(escType, step.EscalationApproverIdentifier, roleIds, userIds);
            }

            if (step.DelegationEnabled && step.DelegationBackupUserId.HasValue)
                userIds.Add(step.DelegationBackupUserId.Value);
        }

        if (roleIds.Count > 0)
        {
            // Roles set is tenant-filtered (TenantId == current OR null system roles); restrict to this tenant.
            var foundRoles = await _db.Roles
                .Where(r => r.TenantId == tenantId && roleIds.Contains(r.Id))
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            var missingRole = roleIds.Except(foundRoles).FirstOrDefault();
            if (missingRole != default)
                return Result.Failure($"Approver role '{missingRole}' does not exist in this tenant.", 400, "invalid_approver");
        }

        if (userIds.Count > 0)
        {
            var foundUsers = await _db.UserTenants
                .IgnoreQueryFilters()
                .Where(ut => ut.TenantId == tenantId && userIds.Contains(ut.UserId))
                .Select(ut => ut.UserId)
                .ToListAsync(cancellationToken);

            var missingUser = userIds.Except(foundUsers).FirstOrDefault();
            if (missingUser != default)
                return Result.Failure($"Approver user '{missingUser}' is not a member of this tenant.", 400, "invalid_approver");
        }

        return Result.Success();
    }

    private static void CollectReference(
        WorkflowApproverType type, Guid? identifier, HashSet<Guid> roleIds, HashSet<Guid> userIds)
    {
        if (!identifier.HasValue)
            return;

        switch (type)
        {
            case WorkflowApproverType.Role:
                roleIds.Add(identifier.Value);
                break;
            case WorkflowApproverType.NamedUser:
                userIds.Add(identifier.Value);
                break;
        }
    }

    /// <summary>
    /// FR-4/AC-4: returns failure with the EXACT plan-limit message when the tenant already has
    /// <see cref="Tenant.MaxWorkflows"/> ACTIVE (non-archived) workflows. Null limit = unlimited.
    /// </summary>
    private async Task<Result> EnsureWithinPlanLimitAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var tenant = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.PlanId, t.MaxWorkflows })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant is null)
            return Result.Success();

        // ISSUE-227/BUG-008: resolve the effective cap with precedence override > plan > snapshot, rather
        // than reading only the Tenant.MaxWorkflows snapshot (which ignored plan changes + overrides).
        var planValue = await _db.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.Code == tenant.PlanId)
            .Select(p => (long?)p.MaxWorkflows)
            .FirstOrDefaultAsync(cancellationToken);
        var overrides = await _db.PlanLimitOverrides
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var resolved = PlanLimitResolver.Resolve(
            PlanLimitKeys.MaxWorkflows, planValue, overrides, DateTime.UtcNow);
        long? limit = resolved.Source == PlanLimitResolver.LimitSource.Override
            ? resolved.Value
            : planValue ?? (long?)tenant.MaxWorkflows;

        if (limit is null)
            return Result.Success();

        var activeCount = await _db.WorkflowDefinitions
            .CountAsync(w => w.Status == WorkflowStatus.Active, cancellationToken);

        if (activeCount >= limit.Value)
            return Result.Failure(
                string.Format(PlanLimitMessageTemplate, limit.Value), 409, "workflow_limit_reached");

        return Result.Success();
    }

    /// <summary>BR-2: archive any currently active workflow for the entity type (optionally excluding one row).</summary>
    private async Task ArchiveActiveForEntityTypeAsync(
        WorkflowEntityType entityType, CancellationToken cancellationToken, Guid? excludeId = null)
    {
        var actives = await _db.WorkflowDefinitions
            .Where(w => w.EntityType == entityType && w.IsActive && (excludeId == null || w.Id != excludeId))
            .ToListAsync(cancellationToken);

        foreach (var active in actives)
        {
            active.IsActive = false;
            active.Status = WorkflowStatus.Archived;
            active.UpdatedAt = DateTime.UtcNow;
        }
    }

    // definitionId is set on the Update path (steps added directly to the DbSet, FK set explicitly); on Create
    // it is left default and EF sets the FK via the parent's Steps navigation. US-ADM-011b: the parallel
    // approver children carry an explicitly-set WorkflowStepId FK (deterministic across InMemory + Postgres,
    // independent of navigation fixup).
    private List<WorkflowStep> MapSteps(
        IReadOnlyList<WorkflowStepRequest> steps, Guid tenantId, Guid definitionId = default)
        => steps
            .OrderBy(s => s.StepOrder)
            .Select(s =>
            {
                var stepId = BaseEntity.NewUuidV7();
                return new WorkflowStep
                {
                    Id = stepId,
                    TenantId = tenantId,
                    WorkflowDefinitionId = definitionId,
                    StepOrder = s.StepOrder,
                    ApproverType = Enum.Parse<WorkflowApproverType>(s.ApproverType, ignoreCase: true),
                    ApproverIdentifier = s.ApproverIdentifier,
                    IsParallel = s.IsParallel,
                    SlaHours = s.SlaHours,
                    EscalationApproverType = s.EscalationApproverType is null
                        ? null
                        : Enum.Parse<WorkflowApproverType>(s.EscalationApproverType, ignoreCase: true),
                    EscalationApproverIdentifier = s.EscalationApproverIdentifier,
                    ConditionJson = string.IsNullOrWhiteSpace(s.ConditionJson) ? null : s.ConditionJson.Trim(),
                    DelegationEnabled = s.DelegationEnabled,
                    DelegationBackupUserId = s.DelegationBackupUserId,
                    CreatedAt = DateTime.UtcNow,
                    // US-ADM-011b: additional NamedUser approvers, only for a parallel step.
                    Approvers = (s.IsParallel && s.ParallelApproverIdentifiers is { Count: > 0 })
                        ? s.ParallelApproverIdentifiers
                            .Where(id => id != Guid.Empty)
                            .Distinct()
                            .Select(id => new WorkflowStepApprover
                            {
                                Id = BaseEntity.NewUuidV7(),
                                TenantId = tenantId,
                                WorkflowStepId = stepId,
                                ApproverType = WorkflowApproverType.NamedUser,
                                ApproverIdentifier = id,
                                CreatedAt = DateTime.UtcNow,
                            })
                            .ToList<WorkflowStepApprover>()
                        : new List<WorkflowStepApprover>(),
                };
            })
            .ToList();

    /// <summary>Saves the change set + writes a before/after audit row (NFR-4).</summary>
    private async Task PersistAsync(
        string eventType, object? before, object? after, Guid workflowId, CancellationToken cancellationToken)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = eventType,
            Action = eventType,
            ResourceType = "WorkflowDefinition",
            ResourceId = workflowId.ToString(),
            Before = before is null ? null : JsonSerializer.Serialize(before),
            After = after is null ? null : JsonSerializer.Serialize(after),
            CreatedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Tenant {TenantId} workflow change: {EventType} ({WorkflowId})",
            _tenantContext.TenantId, eventType, workflowId);
    }

    private static WorkflowDetailDto ToDetailDto(WorkflowDefinition w) => ToDetailDto(w, w.Steps);

    /// <summary>Detail DTO variant taking steps explicitly (Update path — see ToAuditSnapshot overload).</summary>
    private static WorkflowDetailDto ToDetailDto(WorkflowDefinition w, IEnumerable<WorkflowStep> steps) => new(
        w.Id,
        w.LineageId,
        w.Name,
        w.EntityType.ToString(),
        w.Version,
        w.Status.ToString(),
        w.IsActive,
        w.IsDefault,
        w.CreatedAt,
        w.UpdatedAt,
        steps
            .OrderBy(s => s.StepOrder)
            .Select(ToStepDto)
            .ToList());

    private static WorkflowStepDto ToStepDto(WorkflowStep s) => new(
        s.StepOrder,
        s.ApproverType.ToString(),
        s.ApproverIdentifier,
        s.IsParallel,
        s.SlaHours,
        s.EscalationApproverType?.ToString(),
        s.EscalationApproverIdentifier,
        s.ConditionJson,
        s.DelegationEnabled,
        s.DelegationBackupUserId,
        // US-ADM-011b: surface the additional parallel approver ids (null when there are none).
        ParallelApproverIdentifiers: s.Approvers is { Count: > 0 }
            ? s.Approvers.Where(a => a.ApproverIdentifier.HasValue).Select(a => a.ApproverIdentifier!.Value).ToList()
            : null);

    /// <summary>Compact, log-safe snapshot for the audit before/after columns.</summary>
    private static object ToAuditSnapshot(WorkflowDefinition w) => ToAuditSnapshot(w, w.Steps);

    /// <summary>
    /// Snapshot variant that takes the steps explicitly — used on the Update path where the definition is
    /// loaded without its Steps navigation (steps are managed directly via the DbSet to avoid an InMemory
    /// change-tracking fixup conflict).
    /// </summary>
    private static object ToAuditSnapshot(WorkflowDefinition w, IEnumerable<WorkflowStep> steps) => new
    {
        w.Id,
        w.LineageId,
        w.Name,
        EntityType = w.EntityType.ToString(),
        w.Version,
        Status = w.Status.ToString(),
        w.IsActive,
        Steps = steps
            .OrderBy(s => s.StepOrder)
            .Select(s => new
            {
                s.StepOrder,
                ApproverType = s.ApproverType.ToString(),
                s.ApproverIdentifier,
                s.IsParallel,
                s.SlaHours,
                s.ConditionJson,
                EscalationApproverType = s.EscalationApproverType?.ToString(),
                s.DelegationEnabled,
            })
            .ToList(),
    };
}
