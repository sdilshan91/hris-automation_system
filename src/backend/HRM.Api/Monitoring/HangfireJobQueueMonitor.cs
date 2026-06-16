using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Monitoring.DTOs;
using Hangfire;
using Hangfire.Storage;

namespace HRM.Api.Monitoring;

/// <summary>
/// Hangfire-backed <see cref="IJobQueueMonitor"/> (US-ADM-002 FR-5). Reads the cross-tenant job counts from
/// Hangfire's monitoring API (<c>JobStorage.Current.GetMonitoringApi()</c>). Lives in HRM.Api alongside the
/// Hangfire registration. Degrades gracefully: if <c>JobStorage.Current</c> is unset (no Hangfire server
/// configured) or the storage call throws, it returns an <see cref="JobQueueSnapshotDto.Available"/> = false
/// snapshot rather than throwing — so the monitoring dashboard never fails because of background-job storage.
/// </summary>
public sealed class HangfireJobQueueMonitor : IJobQueueMonitor
{
    private static readonly JobQueueSnapshotDto Unavailable =
        new(Available: false, Enqueued: null, Processing: null, Scheduled: null, Succeeded: null, Failed: null);

    public JobQueueSnapshotDto GetSnapshot()
    {
        try
        {
            // JobStorage.Current is only initialised after the Hangfire server starts. Guard it.
            var storage = JobStorage.Current;
            if (storage is null)
                return Unavailable;

            IMonitoringApi api = storage.GetMonitoringApi();
            var stats = api.GetStatistics();

            return new JobQueueSnapshotDto(
                Available: true,
                Enqueued: stats.Enqueued,
                Processing: stats.Processing,
                Scheduled: stats.Scheduled,
                Succeeded: stats.Succeeded,
                Failed: stats.Failed);
        }
        catch (Exception)
        {
            // No storage configured / transient storage failure — report unavailable, never break the dashboard.
            return Unavailable;
        }
    }
}
