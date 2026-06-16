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

        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(tenantId, tenantSubdomain, TenantStatus.Active);

        var runner = scope.ServiceProvider.GetRequiredService<IPayslipDistributionRunner>();
        var result = await runner.RunAsync(runId, targetEmployeeIds, cancellationToken);

        if (result.IsFailure)
            Log.Warning("SendPayslipEmailsJob did not complete. RunId={RunId}, Error={Error}", runId, result.Error);
        else
            Log.Information(
                "Completed SendPayslipEmailsJob. RunId={RunId}, Sent={Sent}, Failed={Failed}, Skipped={Skipped}",
                runId, result.Value!.EmailsSent, result.Value.EmailsFailed, result.Value.EmailsSkipped);
    }
}
