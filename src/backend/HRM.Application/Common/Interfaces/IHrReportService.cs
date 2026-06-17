using HRM.Application.Common.Models;
using HRM.Application.Features.Reports.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Pre-built HR reports (US-RPT-001). Pure read/aggregation over the existing employee / department /
/// location / employment_history tables — NO new entity or migration. Reports compute on-the-fly and
/// are cached in Redis when present (FR-5; the IDistributedCache seam falls back to in-memory).
///
/// <para>Tenant isolation (AC-5, FR-7): every query runs under the EF global query filter
/// (TenantId == ITenantContext.TenantId), so Tenant A can never see Tenant B's data. The tenant id is
/// only read from ITenantContext to build the cache key.</para>
/// </summary>
public interface IHrReportService
{
    /// <summary>Lists the available report types for the FE report-card catalog (US-RPT-001 AC-1).</summary>
    IReadOnlyList<HrReportDescriptorDto> ListReportTypes();

    /// <summary>
    /// Generates a report (metadata + chart data + tabular data) with the supplied filters (FR-1, FR-2).
    /// Served from the Redis cache for repeat queries unless <paramref name="refresh"/> is set (FR-5 / FR-8).
    /// </summary>
    Task<Result<HrReportResult>> GenerateReportAsync(
        HrReportType reportType,
        HrReportQueryParams queryParams,
        bool refresh = false,
        CancellationToken cancellationToken = default);
}
