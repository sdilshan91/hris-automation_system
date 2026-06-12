using HRM.Application.Common.Interfaces;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Daily Hangfire job that checks for employees whose probation period ends within 7 days
/// and logs HR reminders (US-CHR-009 FR-6, AC-4, BR-6).
/// TODO(notification): When the Notification module is built, dispatch actual notifications
/// instead of just logging.
/// </summary>
public sealed class ProbationReminderJob
{
    private readonly IEmployeeStatusService _statusService;

    public ProbationReminderJob(IEmployeeStatusService statusService)
    {
        _statusService = statusService;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting ProbationReminderJob");
        await _statusService.CheckProbationEndDatesAsync();
        Log.Information("Completed ProbationReminderJob");
    }
}
