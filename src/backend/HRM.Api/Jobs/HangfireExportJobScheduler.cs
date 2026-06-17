using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire-backed <see cref="IExportJobScheduler"/> (US-ADM-010 AC-1). Enqueues the tenant-aware
/// <see cref="DataExportGenerationJob"/> to generate the bundle in the background. Lives in HRM.Api alongside the
/// jobs + Hangfire JobStorage; bound to the interface so the Infrastructure export service can enqueue without a
/// Hangfire dependency. When the export service runs without this registration (tests/dev) it skips enqueueing and
/// the caller invokes <c>GenerateAsync</c> directly.
/// </summary>
public sealed class HangfireExportJobScheduler : IExportJobScheduler
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public HangfireExportJobScheduler(IBackgroundJobClient backgroundJobs) => _backgroundJobs = backgroundJobs;

    public void EnqueueGeneration(Guid exportRequestId)
        => _backgroundJobs.Enqueue<DataExportGenerationJob>(job => job.RunAsync(exportRequestId));
}
