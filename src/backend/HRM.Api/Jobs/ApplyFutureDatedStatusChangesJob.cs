using HRM.Application.Common.Interfaces;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Daily Hangfire job that applies future-dated employee status changes
/// whose effective date has arrived (US-CHR-009 BR-4).
/// </summary>
public sealed class ApplyFutureDatedStatusChangesJob
{
    private readonly IEmployeeStatusService _statusService;
    private readonly ITenantContext _tenantContext;

    public ApplyFutureDatedStatusChangesJob(IEmployeeStatusService statusService, ITenantContext tenantContext)
    {
        _statusService = statusService;
        _tenantContext = tenantContext;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting ApplyFutureDatedStatusChangesJob");

        // RLS increment 2c: cross-tenant sweep (the service spans tenants via IgnoreQueryFilters). System
        // context → privileged (BYPASSRLS) routing under RLS + sys: cache prefix.
        _tenantContext.SetSystemContext();

        await _statusService.ApplyPendingFutureDatedChangesAsync();
        Log.Information("Completed ApplyFutureDatedStatusChangesJob");
    }
}
