using HRM.Application.Common.Interfaces;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Daily Hangfire job that checks for employees whose probation period ends within 7 days and dispatches HR
/// reminders (US-CHR-009 FR-6, AC-4, BR-6). US-NTF-006 Phase 7: the reminder is now a real in-app + email
/// notification to the tenant's HR pool (via <c>ICoreHrNotificationService</c>), not just a log line.
/// </summary>
public sealed class ProbationReminderJob
{
    private readonly IEmployeeStatusService _statusService;
    private readonly ITenantContext _tenantContext;

    public ProbationReminderJob(IEmployeeStatusService statusService, ITenantContext tenantContext)
    {
        _statusService = statusService;
        _tenantContext = tenantContext;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting ProbationReminderJob");

        // RLS increment 2c: cross-tenant sweep (the service spans tenants via IgnoreQueryFilters). System
        // context → privileged (BYPASSRLS) routing under RLS + sys: cache prefix.
        _tenantContext.SetSystemContext();

        await _statusService.CheckProbationEndDatesAsync();
        Log.Information("Completed ProbationReminderJob");
    }
}
