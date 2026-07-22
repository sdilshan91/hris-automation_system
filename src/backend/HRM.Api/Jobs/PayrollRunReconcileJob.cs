using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// DF-58: durability backstop for the best-effort post-commit enqueue in <c>PayrollRunService.InitiateAsync</c>.
/// That method commits a run in <see cref="PayrollRunStatus.Queued"/> then fire-and-forgets
/// <c>IPayrollRunJobScheduler.Enqueue</c>. If Hangfire storage is unavailable in that commit→enqueue window the
/// enqueue is lost and the run is stranded in Queued forever — and because the BR-1 partial unique index allows
/// only one active run per period, the stuck run BLOCKS any new initiate for that period until someone cancels
/// it. This recurring sweep re-enqueues runs stranded in Queued so a dropped enqueue self-heals.
///
/// <para><b>Safety — why only Queued+aged.</b> <c>PayrollRun</c> has NO optimistic-concurrency token
/// (last-writer-wins, PayrollRunService ISSUE-154), so re-enqueuing a run a live worker is already
/// <c>Processing</c> (or one in <c>ReviewPending</c>) could race two <c>ProcessAsync</c> executions and corrupt
/// slips. The sweep therefore only re-enqueues runs still in <see cref="PayrollRunStatus.Queued"/> whose
/// <c>InitiatedAt</c> is older than <see cref="StaleThreshold"/> — comfortably past the sub-second fast-path
/// window, where a still-Queued run almost certainly lost its enqueue and has no in-flight job to race.
/// <c>ProcessAsync</c> itself refuses terminal states and only produces ReviewPending slips (it never
/// disburses), so a re-enqueue cannot double-pay.</para>
///
/// <para><b>DF-61 — the dropped-Rerun case IS now auto-healed.</b> A <c>RerunAsync</c> of a ReviewPending run
/// leaves it in ReviewPending and best-effort enqueues a reprocess; a lost enqueue previously stranded it with
/// the OLD slips, undetectably (status-indistinguishable from a correctly-processed run). It now stamps
/// <c>ReprocessRequestedAt</c> (cleared by <c>ProcessAsync</c> on completion), so this sweep can re-enqueue
/// exactly the stranded reruns: a correctly-processed run has a NULL marker (skipped), and a reprocess a worker
/// has ALREADY STARTED is in <c>Processing</c> not <c>ReviewPending</c> (skipped). This closes the
/// status-indistinguishability gap that previously made the Rerun case un-healable. The marker is reset to now
/// on each re-enqueue to bound re-fires to once per threshold.
///
/// <para><b>Residual (unchanged, inherited from the Queued path).</b> Like the DF-58 Queued sweep, this relies
/// on the same 10-min "a worker starts within the threshold" bet, NOT a hard interlock: a rerun job validly
/// enqueued by <c>RerunAsync</c> but still QUEUED (not yet <c>Processing</c>) past <see cref="StaleThreshold"/>
/// would be re-enqueued a second time. <c>ProcessRunJob</c> has no <c>DisableConcurrentExecution</c> and
/// <c>PayrollRun</c> no concurrency token, so two concurrent <c>ProcessAsync</c> could duplicate slips (never
/// double-PAY — <c>ProcessAsync</c> replace-cleans and only produces ReviewPending slips). DF-61 neither
/// introduces nor fixes this shared concurrency window — closing it (a runId-scoped
/// <c>DisableConcurrentExecution</c> or an xmin token) is a separate decision tracked as DF-61-conc.</para>
/// </summary>
public sealed class PayrollRunReconcileJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Only Queued runs older than this are re-enqueued — comfortably past the fast-path enqueue window (normal
    /// processing starts within seconds). Never applied to Processing/ReviewPending (no concurrency token).
    /// Trailing-optional <see cref="TimeProvider"/> keeps this deterministically testable.
    /// </summary>
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(10);

    public PayrollRunReconcileJob(IServiceScopeFactory scopeFactory, TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Log.Information("Starting PayrollRunReconcileJob");

        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - StaleThreshold;

        List<(Guid Id, string Subdomain)> tenants;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenants = (await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => new { t.Id, t.Subdomain })
                .ToListAsync(cancellationToken))
                .Select(t => (t.Id, t.Subdomain))
                .ToList();
        }

        foreach (var (tenantId, subdomain) in tenants)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scheduler = scope.ServiceProvider.GetRequiredService<IPayrollRunJobScheduler>();

                // Run the per-tenant body via the shared runner so the tenant context (and, gated on Rls:Enabled,
                // the app.current_tenant GUC) is set — the EF global query filter then scopes the query below.
                await runner.RunForTenantAsync(tenantId, subdomain, async ct =>
                {
                    // ONLY Queued + aged — NEVER Processing/ReviewPending (no concurrency token; see class doc).
                    var strandedRunIds = await dbContext.PayrollRuns
                        .Where(r => r.Status == PayrollRunStatus.Queued && r.InitiatedAt < cutoff)
                        .Select(r => r.Id)
                        .ToListAsync(ct);

                    foreach (var runId in strandedRunIds)
                    {
                        scheduler.Enqueue(tenantId, subdomain, runId);
                        Log.Warning(
                            "PayrollRunReconcileJob: re-enqueued stranded Queued run {RunId} for tenant {TenantId} " +
                            "(InitiatedAt older than {ThresholdMinutes}m — the post-commit enqueue was likely lost)",
                            runId, tenantId, StaleThreshold.TotalMinutes);
                    }

                    // DF-61: also re-enqueue a ReviewPending run whose reprocess was REQUESTED (RerunAsync stamped
                    // ReprocessRequestedAt) but whose enqueue was dropped. The marker makes this distinguishable
                    // where the raw status could not: a correctly-processed run has a NULL marker (skipped), and a
                    // reprocess a worker has ALREADY STARTED is in Processing, not ReviewPending (skipped). Reset
                    // the marker to now on re-enqueue so the next sweep won't re-fire for another StaleThreshold,
                    // bounding re-enqueues while the reprocess job gets a chance to run + clear it. (Same 10-min
                    // "worker starts in time" bet as the Queued path above — not a hard interlock; see class doc.)
                    var staleReruns = await dbContext.PayrollRuns
                        .Where(r => r.Status == PayrollRunStatus.ReviewPending
                                    && r.ReprocessRequestedAt != null
                                    && r.ReprocessRequestedAt < cutoff)
                        .ToListAsync(ct);

                    foreach (var run in staleReruns)
                    {
                        scheduler.Enqueue(tenantId, subdomain, run.Id);
                        run.ReprocessRequestedAt = _timeProvider.GetUtcNow().UtcDateTime;
                        Log.Warning(
                            "PayrollRunReconcileJob: re-enqueued stale reprocess-requested run {RunId} for tenant " +
                            "{TenantId} (ReprocessRequestedAt older than {ThresholdMinutes}m — the RerunAsync enqueue was likely lost)",
                            run.Id, tenantId, StaleThreshold.TotalMinutes);
                    }
                    if (staleReruns.Count > 0)
                        await dbContext.SaveChangesAsync(ct);
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PayrollRunReconcileJob: Failed to reconcile tenant {TenantId}", tenantId);
                // Continue with other tenants; one tenant's failure must not abort the sweep.
            }
        }

        Log.Information("Completed PayrollRunReconcileJob");
    }
}
