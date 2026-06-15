using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire-backed <see cref="IInterviewReminderScheduler"/> (US-REC-005 FR-4/BR-6). Schedules the
/// tenant-aware <see cref="InterviewReminderJob"/> via <c>IBackgroundJobClient.Schedule</c> at the
/// computed fire-time, and cancels it via <c>Delete</c> on reschedule/cancel. Lives in HRM.Api alongside
/// the job + Hangfire JobStorage; bound to the interface so the Infrastructure interview service can
/// enqueue by interface (mirrors the LeaveReportExportJob seam). When the interview service runs without
/// this registration (tests/dev) it simply skips scheduling.
/// </summary>
public sealed class HangfireInterviewReminderScheduler : IInterviewReminderScheduler
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public HangfireInterviewReminderScheduler(IBackgroundJobClient backgroundJobs)
    {
        _backgroundJobs = backgroundJobs;
    }

    public string? Schedule(Guid tenantId, Guid interviewId, DateTime fireAtUtc)
    {
        // No reminder needed if the fire-time is already in the past (e.g. an interview scheduled inside
        // the lead-time window). The interview itself is still saved.
        if (fireAtUtc <= DateTime.UtcNow)
            return null;

        var delay = fireAtUtc - DateTime.UtcNow;
        return _backgroundJobs.Schedule<InterviewReminderJob>(
            job => job.RunAsync(tenantId, interviewId), delay);
    }

    public void Cancel(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return;

        _backgroundJobs.Delete(jobId);
    }
}
