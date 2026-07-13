using HRM.Application.Common.Interfaces;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// ISSUE-178 PR2: daily Hangfire recurring job that expires payroll-report exports past their 7-day download
/// window and deletes their files. Runs in the system/admin context (cross-tenant). Thin wrapper — the
/// InMemory-testable core lives in <see cref="IPayrollReportExportCleanupService"/>. A 1:1 clone of
/// <see cref="HrReportExportCleanupJob"/>.
/// </summary>
public sealed class PayrollReportExportCleanupJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PayrollReportExportCleanupJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RunAsync()
    {
        using var scope = _scopeFactory.CreateScope();

        // System/admin context so reads/writes span all tenants (cleanup re-scopes per row by tenant id).
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
            mutableContext.SetSystemContext();

        var service = scope.ServiceProvider.GetRequiredService<IPayrollReportExportCleanupService>();
        var result = await service.ExpireOverdueExportsAsync();

        if (result.IsFailure)
            Log.Warning("PayrollReportExportCleanupJob: returned '{Error}'.", result.Error);
        else
            Log.Information("PayrollReportExportCleanupJob: expired {Count} overdue export(s).", result.Value);
    }
}
