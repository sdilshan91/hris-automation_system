using HRM.Application.Features.Attendance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Hangfire background job for large monthly-summary exports (US-ATT-007 FR-7, &gt; 1,000 employees).
///
/// Declared in the Application layer so the Infrastructure <c>AttendanceSummaryService</c> can enqueue
/// it by interface (Hangfire resolves the concrete job in HRM.Api from DI). The job restores the tenant
/// context into its own scope (so the EF global query filter applies), regenerates the summary under the
/// same month/filter, renders the file, stores it via <see cref="IReportExportStorage"/>, and notifies the
/// requester (in-app + email) that the export is ready to download (US-NTF-006, via
/// <see cref="INotificationDispatcher"/>) — completing the FR-7 "download link sent via notification" half.
/// </summary>
public interface IAttendanceSummaryExportJob
{
    Task RunAsync(
        Guid tenantId,
        Guid reportId,
        Guid requestedByUserId,
        int year,
        int month,
        string format,
        MonthlySummaryFilter filter,
        CancellationToken cancellationToken);
}
