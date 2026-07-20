using System.Globalization;
using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-006 Phase 4: the real <see cref="IPayrollNotificationService"/> (US-PAY-003 AC-3 / US-PAY-008 AC-1).
/// Replaces the log-only stub — dispatches payroll-run + approval-workflow notifications (email + in-app) to the
/// resolved recipients via <see cref="INotificationDispatcher"/>. No recipient flows in from the callers, so this
/// impl resolves them:
/// <list type="bullet">
/// <item><b>run-ready / submitted / finalized</b> → every tenant user whose roles grant <c>Payroll.Approve</c>
/// (the approver pool — the same permission gate the approval controller enforces).</item>
/// <item><b>approved / rejected / returned</b> → the run's <c>SubmittedBy</c> (the maker who submitted it); skipped
/// gracefully when null.</item>
/// </list>
///
/// <para>Recipients are real users → both legs use <see cref="NotificationRequest.RecipientUserId"/> (in-app +
/// email). <b>Never throws</b> — a delivery failure must not break the run/approval flow (every path is wrapped;
/// exceptions are swallowed + logged), mirroring the LogOnly contract. Tenant ids are passed explicitly and every
/// resolution query is scoped by tenant id with <c>IgnoreQueryFilters</c> so it is robust outside a request scope.</para>
/// </summary>
public sealed class RealPayrollNotificationService : IPayrollNotificationService
{
    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<RealPayrollNotificationService> _logger;

    public RealPayrollNotificationService(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        ILogger<RealPayrollNotificationService> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task NotifyRunReadyForReviewAsync(
        Guid tenantId, Guid runId, int processed, int skipped, CancellationToken cancellationToken = default)
    {
        try
        {
            var run = await LoadRunAsync(tenantId, runId, cancellationToken);
            var period = PeriodLabel(run);
            var payload = BuildPayload(
                title: "A payroll run is ready for review",
                message: $"The payroll run for {period} has finished computing ({processed} processed, " +
                         $"{skipped} skipped) and is awaiting review.",
                period, processed, skipped, runId);

            var recipients = await ResolveApproverUserIdsAsync(tenantId, cancellationToken);
            await DispatchToUsersAsync(tenantId, "payroll_run_ready", "payroll.run.ready", payload, recipients,
                runId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RealPayrollNotificationService: failed to dispatch run-ready notification for run {RunId} " +
                "(tenant {TenantId}).", runId, tenantId);
        }
    }

    public async Task NotifyApprovalEventAsync(
        Guid tenantId, Guid runId, string eventType, CancellationToken cancellationToken = default)
    {
        try
        {
            // Map the caller's verbatim event type to a catalog event + its recipient rule.
            var (eventKey, notificationType, title, mode) = eventType switch
            {
                "payroll-approval-submitted" => (
                    "payroll_approval_submitted", "payroll.approval.submitted",
                    "A payroll run was submitted for approval", RecipientMode.Approvers),
                "payroll-approval-approved" => (
                    "payroll_approval_approved", "payroll.approval.approved",
                    "Your payroll run was approved", RecipientMode.Submitter),
                "payroll-approval-rejected" => (
                    "payroll_approval_rejected", "payroll.approval.rejected",
                    "Your payroll run was rejected", RecipientMode.Submitter),
                "payroll-approval-returned" => (
                    "payroll_approval_returned", "payroll.approval.returned",
                    "Your payroll run was returned to HR", RecipientMode.Submitter),
                // US-PAY-008 FR-3 (ISSUE-173): recipients = the run's current-step backup approver role holders,
                // falling back to the tenant approver pool when no backup role is configured / has no holders.
                "payroll-approval-escalated" => (
                    "payroll_approval_escalated", "payroll.approval.escalated",
                    "A payroll approval is overdue and was escalated", RecipientMode.BackupRoleOrApprovers),
                // US-PAY-008 FR-6 (ISSUE-173): recipient = the run's DelegatedToUserId (the single effective delegate).
                "payroll-approval-delegated" => (
                    "payroll_approval_delegated", "payroll.approval.delegated",
                    "A payroll approval was delegated to you", RecipientMode.Delegate),
                "payroll-finalized" => (
                    "payroll_finalized", "payroll.finalized",
                    "A payroll run was finalized", RecipientMode.Approvers),
                _ => (string.Empty, string.Empty, string.Empty, RecipientMode.Approvers),
            };

            if (string.IsNullOrEmpty(eventKey))
            {
                _logger.LogWarning(
                    "RealPayrollNotificationService: unknown approval event '{EventType}' for run {RunId} " +
                    "(tenant {TenantId}); nothing dispatched.", eventType, runId, tenantId);
                return;
            }

            var run = await LoadRunAsync(tenantId, runId, cancellationToken);
            var period = PeriodLabel(run);
            var payload = BuildPayload(
                title: title,
                message: $"{title} for the {period} payroll run.",
                period, run?.ProcessedEmployees ?? 0, run?.SkippedEmployees ?? 0, runId);

            IReadOnlyList<Guid> recipients;
            switch (mode)
            {
                case RecipientMode.Approvers:
                    recipients = await ResolveApproverUserIdsAsync(tenantId, cancellationToken);
                    break;

                case RecipientMode.BackupRoleOrApprovers:
                    recipients = await ResolveBackupApproverUserIdsAsync(tenantId, run, cancellationToken);
                    break;

                case RecipientMode.Delegate:
                    // FR-6: the single effective delegate stamped at step activation. Skip gracefully when unset.
                    if (run?.DelegatedToUserId is not { } delegateUser)
                    {
                        _logger.LogWarning(
                            "RealPayrollNotificationService: run {RunId} has no DelegatedToUserId; approval event " +
                            "'{EventType}' not delivered (tenant {TenantId}).", runId, eventType, tenantId);
                        return;
                    }
                    recipients = [delegateUser];
                    break;

                case RecipientMode.Submitter:
                default:
                    // approved / rejected / returned → the run's submitter (maker). Skip gracefully when unknown.
                    if (run?.SubmittedBy is not { } submitter)
                    {
                        _logger.LogWarning(
                            "RealPayrollNotificationService: run {RunId} has no SubmittedBy; approval event " +
                            "'{EventType}' not delivered (tenant {TenantId}).", runId, eventType, tenantId);
                        return;
                    }
                    recipients = [submitter];
                    break;
            }

            await DispatchToUsersAsync(tenantId, eventKey, notificationType, payload, recipients, runId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RealPayrollNotificationService: failed to dispatch approval event '{EventType}' for run {RunId} " +
                "(tenant {TenantId}).", eventType, runId, tenantId);
        }
    }

    private async Task<PayrollRun?> LoadRunAsync(Guid tenantId, Guid runId, CancellationToken cancellationToken)
        => await _db.PayrollRuns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId && r.TenantId == tenantId, cancellationToken);

    /// <summary>
    /// Resolves the distinct ACTIVE tenant users whose assigned roles grant <c>Payroll.Approve</c> (the approver
    /// pool). Scoped by tenant id with IgnoreQueryFilters so it is correct outside a request scope; mirrors the
    /// approval service's eligible-approver query.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveApproverUserIdsAsync(
        Guid tenantId, CancellationToken cancellationToken)
        => await (
                from ut in _db.UserTenants.IgnoreQueryFilters()
                where ut.TenantId == tenantId && ut.Status == UserTenantStatus.Active
                join utr in _db.UserTenantRoles.IgnoreQueryFilters() on ut.Id equals utr.UserTenantId
                join rp in _db.RolePermissions.IgnoreQueryFilters() on utr.RoleId equals rp.RoleId
                where rp.Permission == PermissionCatalog.Payroll.Approve
                select ut.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>
    /// US-PAY-008 FR-3 (ISSUE-173): resolves the escalation recipients — the ACTIVE tenant users holding the run's
    /// CURRENT-step backup approver role (<see cref="HRM.Domain.Entities.PayrollApprovalStepConfig.BackupRoleId"/>).
    /// Falls back to the tenant approver pool when the run has no current step, no configured backup role, or the
    /// backup role has no holders — so an overdue approval is never silently un-notified. Scoped by tenant id with
    /// IgnoreQueryFilters (correct outside a request scope).
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveBackupApproverUserIdsAsync(
        Guid tenantId, PayrollRun? run, CancellationToken cancellationToken)
    {
        if (run?.CurrentApprovalStep is { } step)
        {
            var backupRoleId = await _db.PayrollApprovalStepConfigs.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.StepNumber == step)
                .Select(c => c.BackupRoleId)
                .FirstOrDefaultAsync(cancellationToken);

            if (backupRoleId is { } roleId)
            {
                var holders = await (
                        from ut in _db.UserTenants.IgnoreQueryFilters()
                        where ut.TenantId == tenantId && ut.Status == UserTenantStatus.Active
                        join utr in _db.UserTenantRoles.IgnoreQueryFilters() on ut.Id equals utr.UserTenantId
                        where utr.RoleId == roleId
                        select ut.UserId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                if (holders.Count > 0)
                    return holders;
            }
        }

        // Fallback: the tenant approver pool (Payroll.Approve holders) — the admins who can actually clear the run.
        return await ResolveApproverUserIdsAsync(tenantId, cancellationToken);
    }

    /// <summary>Recipient-resolution strategy for a payroll approval event (US-PAY-008).</summary>
    private enum RecipientMode
    {
        /// <summary>The tenant approver pool (Payroll.Approve holders) — submit / finalize.</summary>
        Approvers,

        /// <summary>The run's submitter (maker) — approved / rejected / returned.</summary>
        Submitter,

        /// <summary>The current step's backup approver role holders, falling back to the approver pool — escalated.</summary>
        BackupRoleOrApprovers,

        /// <summary>The run's single effective delegate (<c>DelegatedToUserId</c>) — delegated (FR-6).</summary>
        Delegate,
    }

    private async Task DispatchToUsersAsync(
        Guid tenantId, string eventKey, string notificationType, string payloadJson,
        IReadOnlyList<Guid> recipientUserIds, Guid runId, CancellationToken cancellationToken)
    {
        if (recipientUserIds.Count == 0)
        {
            _logger.LogWarning(
                "RealPayrollNotificationService: no recipients for event {EventKey} (run {RunId}, tenant {TenantId}); " +
                "nothing delivered.", eventKey, runId, tenantId);
            return;
        }

        foreach (var userId in recipientUserIds)
        {
            var request = new NotificationRequest(
                TenantId: tenantId,
                EventKey: eventKey,
                PayloadJson: payloadJson,
                RecipientUserId: userId,
                NotificationType: notificationType);

            // Both legs are internally never-throw; still guard so one recipient's failure can't drop the rest.
            await _dispatcher.SendInAppAsync(request, cancellationToken);
            await _dispatcher.SendEmailAsync(request, cancellationToken);
        }
    }

    private static string BuildPayload(
        string title, string message, string period, int processed, int skipped, Guid runId) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = title,
            ["message"] = message,
            ["payroll"] = new Dictionary<string, object?>
            {
                ["period"] = period,
                ["processed"] = processed,
                ["skipped"] = skipped,
                ["runId"] = runId.ToString(),
            },
        });

    private static string PeriodLabel(PayrollRun? run)
    {
        if (run is null)
            return "the current period";
        return run.PayMonth is >= 1 and <= 12
            ? $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(run.PayMonth)} {run.PayYear}"
            : $"{run.PayMonth}/{run.PayYear}";
    }
}
