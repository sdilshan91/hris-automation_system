namespace HRM.Application.Common.Interfaces;

/// <summary>
/// ISSUE-178 PR2: the Hangfire background job that generates a large (&ge; 1000-row) payroll-report export. Bound
/// to the concrete job in HRM.Api so the Infrastructure export service can enqueue it by interface without a
/// Hangfire dependency. When the service runs without this registration (tests/dev) it falls back to invoking
/// <see cref="IPayrollReportExportService.GenerateAsync"/> directly, so the flow never requires real Hangfire
/// storage. A 1:1 clone of <see cref="IHrReportExportJob"/>.
/// </summary>
public interface IPayrollReportExportJob
{
    /// <summary>Restores the tenant context, then generates + stores + notifies for the queued export.</summary>
    Task RunAsync(Guid tenantId, Guid exportId, CancellationToken cancellationToken = default);
}

/// <summary>
/// ISSUE-178 PR2: optional Hangfire-backed scheduler seam for the payroll-report-export job — present in HRM.Api
/// (enqueues the job), absent in tests (the service runs generation inline). Kept separate from the other export
/// schedulers so the surfaces never collide.
/// </summary>
public interface IPayrollReportExportJobScheduler
{
    /// <summary>Enqueues background generation of the queued export for the given tenant.</summary>
    void EnqueueGeneration(Guid tenantId, Guid exportId);
}
