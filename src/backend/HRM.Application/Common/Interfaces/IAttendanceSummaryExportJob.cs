using HRM.Application.Features.Attendance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Hangfire background job for large monthly-summary exports (US-ATT-007 FR-7, &gt; 1,000 employees).
///
/// Declared in the Application layer so the Infrastructure <c>AttendanceSummaryService</c> can enqueue
/// it by interface (Hangfire resolves the concrete job in HRM.Api from DI). The job restores the tenant
/// context into its own scope (so the EF global query filter applies), regenerates the summary under the
/// same month/filter, renders the file, and stores it via <see cref="IReportExportStorage"/>.
///
/// DEFERRED: the "download link sent via notification" half of FR-7 — no notification infra exists
/// (US-NTF). The file is stored and the location logged; TODO(US-NTF) marks the dispatch point.
/// </summary>
public interface IAttendanceSummaryExportJob
{
    Task RunAsync(
        Guid tenantId,
        Guid reportId,
        int year,
        int month,
        string format,
        MonthlySummaryFilter filter,
        CancellationToken cancellationToken);
}
