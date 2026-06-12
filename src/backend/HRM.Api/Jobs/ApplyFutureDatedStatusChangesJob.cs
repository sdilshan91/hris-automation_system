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

    public ApplyFutureDatedStatusChangesJob(IEmployeeStatusService statusService)
    {
        _statusService = statusService;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting ApplyFutureDatedStatusChangesJob");
        await _statusService.ApplyPendingFutureDatedChangesAsync();
        Log.Information("Completed ApplyFutureDatedStatusChangesJob");
    }
}
