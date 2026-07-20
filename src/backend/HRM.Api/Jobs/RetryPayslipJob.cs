using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire background job that re-renders + stores the PDF for ONE slip in a payroll run (US-PAY-004 FR-8 /
/// DF-31 / ISSUE-162). The single-slip counterpart to <see cref="GeneratePayslipsJob"/>: it restores the tenant
/// context from the job arguments into its own DI scope (so the EF global query filter scopes every query to the
/// run's tenant — AC-4), then delegates to <see cref="IPayslipBatchRenderer.RenderOneAsync"/>. Idempotent + safely
/// re-runnable: the render overwrites the same GUID-derived storage path.
///
/// <para>Same READ → WORK → WRITE split as the batch job (ISSUE-269) so the render never sits inside one long
/// tenant-GUC transaction: the READ + per-chunk WRITE run via <see cref="ITenantJobRunner"/>, the WORK phase runs
/// outside any runner call. With a single slip there is at most one WRITE chunk.</para>
/// </summary>
public sealed class RetryPayslipJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public RetryPayslipJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>Invoked by Hangfire with the run's tenant + id + the target employee (FR-8).</summary>
    public async Task RunAsync(Guid tenantId, string tenantSubdomain, Guid runId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        Log.Information(
            "Starting RetryPayslipJob. RunId={RunId}, EmployeeId={EmployeeId}, TenantId={TenantId}", runId, employeeId, tenantId);

        using var scope = _scopeFactory.CreateScope();

        var tenantRunner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
        var renderer = scope.ServiceProvider.GetRequiredService<IPayslipBatchRenderer>();

        // ISSUE-269: split into READ → WORK → WRITE so the PDF render + upload never sits inside one long tenant-GUC
        // transaction. The READ and the WRITE run via the shared runner (setting the tenant context — and, gated on
        // Rls:Enabled, the app.current_tenant GUC); the WORK phase runs OUTSIDE any runner call.
        PayslipRenderPlan? plan = null;
        await tenantRunner.RunForTenantAsync(tenantId, tenantSubdomain, async ct =>
        {
            var planResult = await renderer.LoadRenderPlanAsync(runId, employeeId, ct);
            if (planResult.IsSuccess)
                plan = planResult.Value;
            else
                Log.Warning(
                    "RetryPayslipJob did not complete. RunId={RunId}, EmployeeId={EmployeeId}, Error={Error}",
                    runId, employeeId, planResult.Error);
        }, cancellationToken);

        if (plan is null)
            return;

        if (plan.Items.Count == 0)
        {
            Log.Information(
                "Completed RetryPayslipJob. RunId={RunId}, EmployeeId={EmployeeId}, Generated=0, Failed=0", runId, employeeId);
            return;
        }

        // WORK — outside any runner call / tenant-GUC transaction (pure CPU + storage IO).
        var outcomes = await renderer.RenderAndUploadAsync(plan, cancellationToken);

        // WRITE — one short transaction (a single slip is well under the batch chunk size).
        await tenantRunner.RunForTenantAsync(tenantId, tenantSubdomain,
            ct => renderer.PersistRenderResultsAsync(outcomes, ct), cancellationToken);

        var generated = outcomes.Count(o => o.Ok);
        var failed = outcomes.Count(o => !o.Ok);
        Log.Information(
            "Completed RetryPayslipJob. RunId={RunId}, EmployeeId={EmployeeId}, Generated={Generated}, Failed={Failed}",
            runId, employeeId, generated, failed);
    }
}
