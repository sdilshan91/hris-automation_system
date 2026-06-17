using HRM.Application.Common.Models;
using HRM.Application.Features.Reports.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-RPT-004: export of the generic HR / leave / attendance reports (US-RPT-001 / US-RPT-002, served at
/// <c>api/v1/reports</c>) to CSV / Excel / PDF. SEPARATE from the payroll export (US-PAY-009) and the
/// US-ADM-010 tenant data export.
///
/// <para>Tenant isolation (AC-5 / NFR-7): every read/write runs under the EF global query filter
/// (TenantId == ITenantContext.TenantId). Initiation regenerates the report to learn the row count, then
/// routes small reports (&lt; 1000 rows, FR-5) to an inline render + store + complete, and large reports
/// (&ge; 1000 rows) to a Hangfire job (HrReportExportJob). FR-10 limits each user to 3 in-progress exports.
/// Every export is audited (FR-9).</para>
/// </summary>
public interface IHrReportExportService
{
    /// <summary>
    /// Initiates an export (FR-5). Validates the report type + format, enforces the FR-10 per-user concurrency
    /// limit (returns a 429 failure when the caller already has &ge; 3 Queued/Processing exports), regenerates
    /// the report to learn the row count, then either renders inline (sync, &lt; 1000 rows → Completed) or
    /// enqueues a Hangfire job (async, &ge; 1000 rows → Queued). Audits the action (FR-9).
    /// </summary>
    Task<Result<HrReportExportInitiatedDto>> InitiateAsync(
        string reportType,
        HrReportQueryParams filters,
        string format,
        bool includeCharts,
        CancellationToken cancellationToken = default);

    /// <summary>The current user's recent exports, newest-first (the FR-9 / §8 history list).</summary>
    Task<Result<IReadOnlyList<HrReportExportListItemDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored file bytes for a download (AC-5). Tenant-scoped (global filter) + owner check.
    /// Returns <c>null</c> when the export is missing, cross-tenant, or not owned by the caller (controller →
    /// 404/403); returns an <see cref="HrReportExportDownloadDto"/> with <c>Expired=true</c> when it exists but
    /// has been purged/expired (controller → 410); otherwise the file. Audits the download ("HrReport.ExportDownloaded").
    /// </summary>
    Task<Result<HrReportExportDownloadDto?>> GetForDownloadAsync(
        Guid exportId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a Queued export NOW (invoked by the Hangfire HrReportExportJob, or directly in tests): sets
    /// Processing, regenerates the report under the captured tenant context, renders, stores, sets Completed +
    /// ExpiresAt + sizes, notifies the requester (FR-8); on exception sets Failed + Error. Tenant id is explicit
    /// so it works from the background job's own scope.
    /// </summary>
    Task<Result> GenerateAsync(Guid exportId, CancellationToken cancellationToken = default);
}
