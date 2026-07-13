using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// ISSUE-178 PR2: export of the payroll reports (US-PAY-009 / US-RPT-003, served at
/// <c>api/v1/payroll/reports</c>) to CSV / Excel / PDF with a sync fast-path + an async Hangfire path for large
/// reports. A deliberate 1:1 clone of <see cref="IHrReportExportService"/>. SEPARATE from the generic-report
/// export (US-RPT-004), the leave report export, and the US-ADM-010 tenant data export.
///
/// <para>Tenant isolation (AC-5): every read/write runs under the EF global query filter
/// (TenantId == ITenantContext.TenantId). Initiation regenerates the report to learn the row count, then routes
/// small reports (&lt; 1000 rows) to an inline render + store + complete, and large reports (&ge; 1000 rows) to a
/// Hangfire job (PayrollReportExportJob). Each user is limited to 3 in-progress exports; every export is audited.</para>
/// </summary>
public interface IPayrollReportExportService
{
    /// <summary>
    /// Initiates an export. Validates the report type + format, enforces the per-user concurrency limit
    /// (returns a 429 failure when the caller already has &ge; 3 Queued/Processing exports), regenerates the
    /// report to learn the row count, then either renders inline (sync, &lt; 1000 rows → Completed) or enqueues a
    /// Hangfire job (async, &ge; 1000 rows → Queued). Audits the action.
    /// </summary>
    Task<Result<PayrollReportExportInitiatedDto>> InitiateAsync(
        string reportType,
        PayrollReportQueryParams filters,
        string format,
        CancellationToken cancellationToken = default);

    /// <summary>The current user's recent exports, newest-first (the history list).</summary>
    Task<Result<IReadOnlyList<PayrollReportExportListItemDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored file bytes for a download (AC-5). Tenant-scoped (global filter) + owner check. Returns
    /// <c>null</c> when the export is missing, cross-tenant, or not owned by the caller (controller → 404);
    /// returns a <see cref="PayrollReportExportDownloadDto"/> with <c>Expired=true</c> when it exists but has been
    /// purged/expired (controller → 410); otherwise the file. Audits the download ("PayrollReport.ExportDownloaded").
    /// </summary>
    Task<Result<PayrollReportExportDownloadDto?>> GetForDownloadAsync(
        Guid exportId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a Queued export NOW (invoked by the Hangfire PayrollReportExportJob, or directly in tests): sets
    /// Processing, regenerates the report under the captured tenant context, renders, stores, sets Completed +
    /// ExpiresAt + sizes, notifies the requester; on exception sets Failed + Error. The export id is explicit so
    /// it works from the background job's own scope.
    /// </summary>
    Task<Result> GenerateAsync(Guid exportId, CancellationToken cancellationToken = default);
}
