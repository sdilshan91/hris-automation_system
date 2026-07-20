using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-PAY-008 FR-3 (ISSUE-173): escalates payroll-run approvals whose current-step SLA has elapsed, in the current
/// tenant context. Ports the generic <c>WorkflowSlaEscalator</c> shape into the payroll slice (payroll keeps its own
/// maker-checker / distinct-approver / step-role guards in <see cref="PayrollApprovalService"/>, so it deliberately
/// does NOT route through the shared workflow runtime).
///
/// <para>Idempotent via a conditional <c>ExecuteUpdateAsync</c> compare-and-swap (WHERE Status = AwaitingApproval AND
/// EscalatedAt IS NULL) — at-most-once even across overlapping runs, with NO nested transaction (it composes safely
/// whether or not the caller's <c>ITenantJobRunner</c> wraps it in a tx under RLS).</para>
///
/// <para>Escalation is a NOTIFY, not a hard reassignment: the breached run is stamped <c>EscalatedAt</c>, an
/// <c>Escalated</c> <see cref="PayrollApprovalHistory"/> row is written, and the current step's backup approver role
/// (or the tenant approver pool when none is configured) is notified via <see cref="IPayrollNotificationService"/>.
/// The run stays AwaitingApproval so any holder of the step/backup role can act via the pending-approvals queue.</para>
/// </summary>
public sealed class PayrollApprovalSlaEscalator : IPayrollApprovalSlaEscalator
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPayrollNotificationService _notifications;
    private readonly ILogger<PayrollApprovalSlaEscalator> _logger;
    private readonly TimeProvider _timeProvider;

    public PayrollApprovalSlaEscalator(
        AppDbContext db,
        ITenantContext tenantContext,
        IPayrollNotificationService notifications,
        ILogger<PayrollApprovalSlaEscalator> logger,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _logger = logger;
        // FR-3 (ISSUE-173): injectable clock so a test can advance time to force a breach (DF-43). Trailing-optional
        // + TimeProvider.System default so manual test-DI constructions keep compiling; DI supplies the real one.
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<int> EscalateDueStepsAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return 0;

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Tenant-filtered by the global query filter. Only AwaitingApproval, past-SLA, not-yet-escalated runs are due.
        var due = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.Status == PayrollRunStatus.AwaitingApproval
                        && r.SlaDueAt != null && r.SlaDueAt < now && r.EscalatedAt == null)
            .Select(r => new { r.Id, r.CurrentApprovalStep, r.CurrentWorkflowInstanceId })
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
            return 0;

        var escalated = 0;
        foreach (var run in due)
        {
            // CAS: win the escalation atomically — stamp EscalatedAt. Idempotent across overlapping runs (a second
            // pass sees EscalatedAt != null and the guard makes the update a no-op).
            var won = await _db.PayrollRuns
                .Where(r => r.Id == run.Id
                            && r.Status == PayrollRunStatus.AwaitingApproval
                            && r.EscalatedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.EscalatedAt, now), cancellationToken);
            if (won == 0)
                continue; // another run already escalated it

            // FR-7/NFR-5: immutable audit row. The actor is the system/job (no acting user) → Guid.Empty.
            _db.PayrollApprovalHistories.Add(new PayrollApprovalHistory
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                PayrollRunId = run.Id,
                WorkflowInstanceId = run.CurrentWorkflowInstanceId ?? BaseEntity.NewUuidV7(),
                StepNumber = run.CurrentApprovalStep ?? 1,
                Action = PayrollApprovalAction.Escalated,
                ActorUserId = Guid.Empty,
                Comments = $"Approval SLA breached on step {run.CurrentApprovalStep ?? 1}; escalated to the backup approver role.",
                ActedAt = now,
                IpAddress = null,
            });
            await _db.SaveChangesAsync(cancellationToken);

            // NOTIFY the current step's backup approver role (fallback approver pool). Never-throw seam.
            await _notifications.NotifyApprovalEventAsync(
                _tenantContext.TenantId, run.Id, "payroll-approval-escalated", cancellationToken);

            escalated++;
        }

        if (escalated > 0)
            _logger.LogInformation(
                "PayrollApprovalSlaEscalator escalated {Count} breached payroll approval(s) for tenant {TenantId}.",
                escalated, _tenantContext.TenantId);

        return escalated;
    }
}
