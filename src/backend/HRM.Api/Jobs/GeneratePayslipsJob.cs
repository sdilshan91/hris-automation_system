using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire background job that renders + stores the PDF payslips for a payroll run (US-PAY-004 FR-4). The
/// worker does NOT pass through TenantResolutionMiddleware, so it restores the tenant context from the job
/// arguments into its own DI scope (so the EF global query filter scopes every query to the run's tenant —
/// AC-4), then delegates the heavy batch render to <see cref="IPayslipBatchRenderer"/>. Idempotent + safely
/// re-runnable: regeneration overwrites the same GUID-derived storage paths (AC-5).
///
/// <para>Uses <see cref="IServiceScopeFactory"/> (mirrors AttendanceSummaryExportJob) because the renderer +
/// DbContext + tenant context are scoped services and the job itself is resolved once by Hangfire.</para>
/// </summary>
public sealed class GeneratePayslipsJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public GeneratePayslipsJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>Invoked by Hangfire with the run's tenant + id (FR-4).</summary>
    public async Task RunAsync(Guid tenantId, string tenantSubdomain, Guid runId, CancellationToken cancellationToken = default)
    {
        Log.Information("Starting GeneratePayslipsJob. RunId={RunId}, TenantId={TenantId}", runId, tenantId);

        using var scope = _scopeFactory.CreateScope();

        var tenantRunner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var renderer = scope.ServiceProvider.GetRequiredService<IPayslipBatchRenderer>();

        // ISSUE-269: split the batch render into READ → WORK → WRITE so the heavy PDF render + upload never sits
        // inside one long tenant-GUC transaction (RLS idle-in-transaction). The READ and each per-chunk WRITE run
        // via the shared runner (so the tenant context — and, gated on Rls:Enabled, the app.current_tenant GUC —
        // is set for the DB work); the WORK phase runs OUTSIDE any runner call, so no transaction is held idle
        // through the minutes-long render. Under RLS-off the runner no-ops the transaction, so this is identical
        // to the prior single-call behaviour.
        PayslipRenderPlan? plan = null;
        await tenantRunner.RunForTenantAsync(tenantId, tenantSubdomain, async ct =>
        {
            var planResult = await renderer.LoadRenderPlanAsync(runId, ct);
            if (planResult.IsSuccess)
                plan = planResult.Value;
            else
                Log.Warning("GeneratePayslipsJob did not complete. RunId={RunId}, Error={Error}", runId, planResult.Error);
        }, cancellationToken);

        if (plan is null)
            return;

        if (plan.Items.Count == 0)
        {
            Log.Information("Completed GeneratePayslipsJob. RunId={RunId}, Generated=0, Failed=0", runId);
            return;
        }

        // WORK — outside any runner call / tenant-GUC transaction (pure CPU + storage IO).
        var outcomes = await renderer.RenderAndUploadAsync(plan, cancellationToken);

        // WRITE — commit each chunk in its own short transaction (chunk of 200; no run-level aggregate to keep atomic).
        foreach (var chunk in outcomes.Chunk(WriteChunkSize))
        {
            await tenantRunner.RunForTenantAsync(tenantId, tenantSubdomain,
                ct => renderer.PersistRenderResultsAsync(chunk, ct), cancellationToken);
        }

        var generated = outcomes.Count(o => o.Ok);
        var failed = outcomes.Count(o => !o.Ok);
        Log.Information(
            "Completed GeneratePayslipsJob. RunId={RunId}, Generated={Generated}, Failed={Failed}",
            runId, generated, failed);
    }

    /// <summary>ISSUE-269: slips per WRITE transaction. Free to pick (no run-level aggregate to keep atomic).</summary>
    private const int WriteChunkSize = 200;
}
