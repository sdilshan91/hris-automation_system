using HRM.Application.Common.Interfaces;
using Hangfire;

namespace HRM.Api.Jobs;

/// <summary>
/// ISSUE-178 PR2: Hangfire-backed <see cref="IPayrollReportExportJobScheduler"/>. Enqueues the tenant-aware
/// <see cref="PayrollReportExportJob"/> to generate a large payroll-report export in the background. Lives in
/// HRM.Api alongside the jobs + Hangfire JobStorage; bound to the interface so the Infrastructure export service
/// can enqueue without a Hangfire dependency. When the export service runs without this registration (tests/dev)
/// it skips enqueueing and the caller invokes <c>GenerateAsync</c> directly. A 1:1 clone of
/// <see cref="HangfireHrReportExportJobScheduler"/>.
/// </summary>
public sealed class HangfirePayrollReportExportJobScheduler : IPayrollReportExportJobScheduler
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public HangfirePayrollReportExportJobScheduler(IBackgroundJobClient backgroundJobs) => _backgroundJobs = backgroundJobs;

    public void EnqueueGeneration(Guid tenantId, Guid exportId)
        => _backgroundJobs.Enqueue<PayrollReportExportJob>(job => job.RunAsync(tenantId, exportId, CancellationToken.None));
}
