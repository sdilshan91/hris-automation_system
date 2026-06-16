using HRM.Application.Features.Monitoring.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Cross-tenant background-job queue snapshot (US-ADM-002 FR-5). Abstracts Hangfire's
/// <c>JobStorage.Current.GetMonitoringApi()</c> behind an interface so the monitoring service does not
/// hard-depend on a running Hangfire server: the real implementation lives in HRM.Api (alongside Hangfire),
/// and tests substitute it. When Hangfire storage is unavailable the implementation returns a snapshot with
/// <see cref="JobQueueSnapshotDto.Available"/> = false rather than throwing.
/// </summary>
public interface IJobQueueMonitor
{
    /// <summary>Returns the current queued/processing/scheduled/succeeded/failed counts, or an Available=false snapshot.</summary>
    JobQueueSnapshotDto GetSnapshot();
}
