namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-010 (AC-1): the OPTIONAL Hangfire seam used by <see cref="ITenantDataExportService"/> to enqueue
/// background generation of an export bundle. It is registered only in HRM.Api alongside Hangfire, so the export
/// service never hard-depends on a running Hangfire server — in tests/dev (where it is absent) the caller invokes
/// <see cref="ITenantDataExportService.GenerateAsync"/> directly. Mirrors the optional scheduler seams used by the
/// payroll-run / payslip-generation / offer-expiry services.
/// </summary>
public interface IExportJobScheduler
{
    /// <summary>Enqueues a background job that calls <c>GenerateAsync(exportRequestId)</c> in a fresh tenant scope.</summary>
    void EnqueueGeneration(Guid exportRequestId);
}
