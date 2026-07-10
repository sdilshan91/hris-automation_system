using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-011b (AC-5/FR-8/BR-4/NFR-3): escalates approval steps whose SLA has elapsed, in the current tenant
/// context. Idempotent via a conditional <see cref="RelationalQueryableExtensions"/> ExecuteUpdate compare-and-swap
/// (WHERE Decision = Pending AND EscalatedAt IS NULL) — at-most-once even across overlapping runs, with NO nested
/// transaction (it composes safely whether or not the caller's <c>ITenantJobRunner</c> wraps it in a tx under RLS).
///
/// <para>When the step defines an escalation target, the breached row is closed (Escalated) and a fresh Pending row
/// is opened for the escalation approver at the same order. When there is NO configured target, the row stays
/// Pending (the original approver can still act) but is stamped <c>EscalatedAt</c> so it is not re-processed, and
/// the tenant admins are notified.</para>
/// </summary>
public sealed class WorkflowSlaEscalator : IWorkflowSlaEscalator
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<WorkflowSlaEscalator> _logger;
    private readonly INotificationDispatcher? _dispatcher;

    public WorkflowSlaEscalator(
        AppDbContext db,
        ITenantContext tenantContext,
        ILogger<WorkflowSlaEscalator> logger,
        INotificationDispatcher? dispatcher = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
        _dispatcher = dispatcher;
    }

    public async Task<int> EscalateDueStepsAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return 0;

        var now = DateTime.UtcNow;

        // Tenant-filtered by the global query filter. Only Pending, past-SLA, not-yet-escalated rows are due.
        var due = await _db.WorkflowStepInstances
            .Where(s => s.Decision == WorkflowStepDecision.Pending
                        && s.SlaDueAt != null && s.SlaDueAt < now && s.EscalatedAt == null)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
            return 0;

        // Only escalate steps whose instance is still InProgress.
        var instanceIds = due.Select(s => s.WorkflowInstanceId).Distinct().ToList();
        var liveInstances = await _db.WorkflowInstances
            .Where(i => instanceIds.Contains(i.Id) && i.Status == WorkflowInstanceStatus.InProgress)
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var escalated = 0;
        foreach (var step in due)
        {
            if (!liveInstances.TryGetValue(step.WorkflowInstanceId, out var instance))
                continue;

            // Load the definition step for this order to read the escalation target (AsNoTracking — read-only).
            var def = await _db.WorkflowSteps.AsNoTracking()
                .FirstOrDefaultAsync(
                    ws => ws.WorkflowDefinitionId == instance.WorkflowDefinitionId && ws.StepOrder == step.StepOrder,
                    cancellationToken);

            var hasTarget = def?.EscalationApproverType is not null;

            if (hasTarget)
            {
                // CAS: win the escalation atomically — close the breached row. Idempotent across overlapping runs.
                var won = await _db.WorkflowStepInstances
                    .Where(s => s.Id == step.Id && s.Decision == WorkflowStepDecision.Pending && s.EscalatedAt == null)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(x => x.Decision, WorkflowStepDecision.Escalated)
                        .SetProperty(x => x.EscalatedAt, now), cancellationToken);
                if (won == 0)
                    continue; // another run already escalated it

                var targetUser = await ResolveApproverAsync(
                    def!.EscalationApproverType!.Value, def.EscalationApproverIdentifier, instance, cancellationToken);

                _db.WorkflowStepInstances.Add(new WorkflowStepInstance
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = instance.TenantId,
                    WorkflowInstanceId = instance.Id,
                    StepOrder = step.StepOrder,
                    IsParallel = step.IsParallel,
                    ApproverType = def.EscalationApproverType.Value,
                    ApproverIdentifier = def.EscalationApproverIdentifier,
                    AssignedApproverUserId = targetUser,
                    Decision = WorkflowStepDecision.Pending,
                    SlaDueAt = def.SlaHours > 0 ? now.AddHours(def.SlaHours) : null,
                    CreatedAt = now,
                });

                AddAudit(instance, "workflow.step.escalated",
                    $"Step {step.StepOrder} SLA breached; escalated to {def.EscalationApproverType}.");
                await _db.SaveChangesAsync(cancellationToken);

                if (targetUser is Guid tu)
                    await DispatchToUserAsync(instance, step.StepOrder, tu, cancellationToken);
                else
                    await DispatchToTenantAdminsAsync(instance, step.StepOrder, cancellationToken);

                escalated++;
            }
            else
            {
                // No configured target → keep the row Pending (the original approver can still act) but stamp
                // EscalatedAt so it is not re-processed, and notify the tenant admins. CAS on EscalatedAt only.
                var won = await _db.WorkflowStepInstances
                    .Where(s => s.Id == step.Id && s.Decision == WorkflowStepDecision.Pending && s.EscalatedAt == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(x => x.EscalatedAt, now), cancellationToken);
                if (won == 0)
                    continue;

                AddAudit(instance, "workflow.step.escalated",
                    $"Step {step.StepOrder} SLA breached; no escalation target — Tenant Admin notified.");
                await _db.SaveChangesAsync(cancellationToken);

                await DispatchToTenantAdminsAsync(instance, step.StepOrder, cancellationToken);
                escalated++;
            }
        }

        if (escalated > 0)
            _logger.LogInformation(
                "WorkflowSlaEscalator escalated {Count} breached step(s) for tenant {TenantId}.",
                escalated, _tenantContext.TenantId);

        return escalated;
    }

    /// <summary>
    /// Resolves an escalation approver spec to a concrete user id (mirrors WorkflowRuntimeService's resolution).
    /// NamedUser → identifier; LineManager/DepartmentHead → resolved via the requester's employee record;
    /// Role → null (role holders are authorized dynamically at decision time).
    /// </summary>
    private async Task<Guid?> ResolveApproverAsync(
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
                return null;
        }
    }

    /// <summary>Notify a single resolved escalation target. Never-throw.</summary>
    private async Task DispatchToUserAsync(
        WorkflowInstance instance, int stepOrder, Guid userId, CancellationToken cancellationToken)
    {
        if (_dispatcher is null || userId == Guid.Empty)
            return;
        await SendAsync(instance, stepOrder, userId, cancellationToken);
    }

    /// <summary>
    /// Notify the tenant admins (holders of <c>Tenant.ManageWorkflows</c>) — used when a breached step has no
    /// configured escalation target. Never-throw; logs when no admin is found.
    /// </summary>
    private async Task DispatchToTenantAdminsAsync(
        WorkflowInstance instance, int stepOrder, CancellationToken cancellationToken)
    {
        if (_dispatcher is null)
            return;

        var adminUserIds = await (
                from ut in _db.UserTenants.IgnoreQueryFilters()
                where ut.TenantId == instance.TenantId && ut.Status == UserTenantStatus.Active
                join utr in _db.UserTenantRoles.IgnoreQueryFilters() on ut.Id equals utr.UserTenantId
                join rp in _db.RolePermissions.IgnoreQueryFilters() on utr.RoleId equals rp.RoleId
                where rp.Permission == PermissionCatalog.Tenant.ManageWorkflows
                select ut.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (adminUserIds.Count == 0)
        {
            _logger.LogWarning(
                "WorkflowSlaEscalator: no tenant admin to notify for escalated step {StepOrder} (instance {InstanceId}, tenant {TenantId}).",
                stepOrder, instance.Id, instance.TenantId);
            return;
        }

        foreach (var userId in adminUserIds)
            await SendAsync(instance, stepOrder, userId, cancellationToken);
    }

    private async Task SendAsync(
        WorkflowInstance instance, int stepOrder, Guid userId, CancellationToken cancellationToken)
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
                },
            });
            var request = new NotificationRequest(
                TenantId: instance.TenantId,
                EventKey: "workflow_step_escalated",
                PayloadJson: payloadJson,
                RecipientUserId: userId,
                RecipientEmail: null,
                NotificationType: null);

            await _dispatcher!.SendInAppAsync(request, cancellationToken);
            await _dispatcher.SendEmailAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Workflow escalation notification for user {UserId} (instance {InstanceId}) failed (non-fatal).",
                userId, instance.Id);
        }
    }

    private void AddAudit(WorkflowInstance instance, string eventType, string detail)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = instance.TenantId,
            UserId = null,
            EventType = eventType,
            Action = eventType,
            ResourceType = "WorkflowInstance",
            ResourceId = instance.Id.ToString(),
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }
}
