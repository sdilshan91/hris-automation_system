using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire background job that emails the PDF payslips for a FINALIZED payroll run (US-PAY-011 FR-1/FR-8). The
/// worker does NOT pass through TenantResolutionMiddleware, so it restores the tenant context from the job
/// arguments into its own DI scope (so the EF global query filter scopes every query to the run's tenant —
/// AC-5), then delegates the per-employee send loop to <see cref="IPayslipDistributionRunner"/>. Idempotent +
/// resumable: the runner skips employees already Sent (NFR-3), so re-running after a partial failure picks up
/// where it left off.
///
/// <para><paramref name="targetEmployeeIds"/> is null for a full send and a non-empty list for a selective
/// re-send (FR-4). Uses <see cref="IServiceScopeFactory"/> (mirrors GeneratePayslipsJob) because the runner +
/// DbContext + tenant context are scoped.</para>
/// </summary>
public sealed class SendPayslipEmailsJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SendPayslipEmailsJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>Invoked by Hangfire with the run's tenant + id and the optional re-send target set (FR-8).</summary>
    public async Task RunAsync(
        Guid tenantId, string tenantSubdomain, Guid runId, List<Guid>? targetEmployeeIds,
        CancellationToken cancellationToken = default)
    {
        Log.Information(
            "Starting SendPayslipEmailsJob. RunId={RunId}, TenantId={TenantId}, Targeted={Targeted}",
            runId, tenantId, targetEmployeeIds?.Count.ToString() ?? "all");

        using var scope = _scopeFactory.CreateScope();

        var tenantRunner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var runner = scope.ServiceProvider.GetRequiredService<IPayslipDistributionRunner>();

        // ISSUE-269: split the send loop into READ → per-item WORK → per-send WRITE so no SMTP send sits inside a
        // long tenant-GUC transaction (RLS idle-in-transaction), and each result is committed per-send. The READ
        // and each per-send WRITE run via the shared runner (tenant context + GUC gated on Rls:Enabled); the SMTP
        // send runs OUTSIDE any runner call. Committing per-send also fixes the crash-resumability bug: a mid-batch
        // crash keeps the already-sent rows (previously all log rows were saved only at the very end). Under RLS-off
        // the runner no-ops the transaction, so this is identical to the prior single-call behaviour.
        PayslipSendPlan? plan = null;
        await tenantRunner.RunForTenantAsync(tenantId, tenantSubdomain, async ct =>
        {
            var planResult = await runner.LoadSendPlanAsync(runId, targetEmployeeIds, ct);
            if (planResult.IsSuccess)
                plan = planResult.Value;
            else
                Log.Warning("SendPayslipEmailsJob did not complete. RunId={RunId}, Error={Error}", runId, planResult.Error);
        }, cancellationToken);

        if (plan is null)
            return;

        var sent = 0;
        var failed = 0;
        var skipped = 0;
        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // NFR-3 / resume: never re-send an employee already Sent for this run.
            if (plan.AlreadySent.Contains(item.EmployeeId))
                continue;

            // WORK — SMTP send outside any runner call / tenant-GUC transaction.
            var outcome = await runner.SendOneAsync(item, cancellationToken);

            // WRITE — commit this one send in its own short transaction (resumability).
            await tenantRunner.RunForTenantAsync(tenantId, tenantSubdomain,
                ct => runner.PersistSendOutcomeAsync(outcome, ct), cancellationToken);

            if (outcome.Status == HRM.Domain.Payroll.EmailDeliveryStatus.Sent) sent++;
            else if (outcome.Status == HRM.Domain.Payroll.EmailDeliveryStatus.Skipped) skipped++;
            else failed++;
        }

        Log.Information(
            "Completed SendPayslipEmailsJob. RunId={RunId}, Sent={Sent}, Failed={Failed}, Skipped={Skipped}",
            runId, sent, failed, skipped);
    }
}
