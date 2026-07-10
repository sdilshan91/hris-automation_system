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

        // RLS increment 2c: run the render via the shared runner so it sets the tenant context (and, gated on
        // Rls:Enabled, the app.current_tenant GUC) — this payroll-run-by-id job stays inside the RLS backstop.
        await tenantRunner.RunForTenantAsync(tenantId, tenantSubdomain, async ct =>
        {
            var result = await renderer.RenderRunAsync(runId, ct);

            if (result.IsFailure)
                Log.Warning("GeneratePayslipsJob did not complete. RunId={RunId}, Error={Error}", runId, result.Error);
            else
                Log.Information(
                    "Completed GeneratePayslipsJob. RunId={RunId}, Generated={Generated}, Failed={Failed}",
                    runId, result.Value!.Generated, result.Value.Failed);
        }, cancellationToken);
    }
}
