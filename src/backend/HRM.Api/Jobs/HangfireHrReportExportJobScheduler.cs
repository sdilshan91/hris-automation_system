using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// US-RPT-004: Hangfire-backed <see cref="IHrReportExportJobScheduler"/>. Enqueues the tenant-aware
/// <see cref="HrReportExportJob"/> to generate a large report export in the background. Lives in HRM.Api
/// alongside the jobs + Hangfire JobStorage; bound to the interface so the Infrastructure export service can
/// enqueue without a Hangfire dependency. When the export service runs without this registration (tests/dev)
/// it skips enqueueing and the caller invokes <c>GenerateAsync</c> directly.
/// </summary>
public sealed class HangfireHrReportExportJobScheduler : IHrReportExportJobScheduler
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public HangfireHrReportExportJobScheduler(IBackgroundJobClient backgroundJobs) => _backgroundJobs = backgroundJobs;

    public void EnqueueGeneration(Guid tenantId, Guid exportId)
        => _backgroundJobs.Enqueue<HrReportExportJob>(job => job.RunAsync(tenantId, exportId, CancellationToken.None));
}
