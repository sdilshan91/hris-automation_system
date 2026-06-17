namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-RPT-004 (FR-5/FR-8): the Hangfire background job that generates a large (&ge; 1000-row) report export.
/// Bound to the concrete job in HRM.Api so the Infrastructure export service can enqueue it by interface without
/// a Hangfire dependency. When the service runs without this registration (tests/dev) it falls back to invoking
/// <see cref="IHrReportExportService.GenerateAsync"/> directly, so the flow never requires real Hangfire storage.
/// </summary>
public interface IHrReportExportJob
{
    /// <summary>Restores the tenant context, then generates + stores + notifies for the queued export.</summary>
    Task RunAsync(Guid tenantId, Guid exportId, CancellationToken cancellationToken = default);
}

/// <summary>
/// US-RPT-004: optional Hangfire-backed scheduler seam for the report-export job. Mirrors the US-ADM-010
/// <c>IExportJobScheduler</c> pattern — present in HRM.Api (enqueues the job), absent in tests (the service runs
/// generation inline). Kept separate from <c>IExportJobScheduler</c> so the two export surfaces never collide.
/// </summary>
public interface IHrReportExportJobScheduler
{
    /// <summary>Enqueues background generation of the queued export for the given tenant.</summary>
    void EnqueueGeneration(Guid tenantId, Guid exportId);
}
