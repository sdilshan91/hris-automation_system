using HRM.Application.Features.LeaveReports.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Hangfire background job that generates a large leave-report export (US-LV-012 FR-5, AC-5).
///
/// Declared in the Application layer so the Infrastructure <c>LeaveReportService</c> can enqueue it
/// by interface (Hangfire resolves the concrete <c>LeaveReportExportJob</c>, which lives in HRM.Api,
/// from DI). The job re-runs the report under the same tenant/scope/filters, renders the file, stores
/// it via <see cref="IReportExportStorage"/>, and notifies the requester the export is ready.
/// </summary>
public interface ILeaveReportExportJob
{
    /// <summary>
    /// Regenerates and stores a report export. The tenant is restored into the job's scope so the EF
    /// global query filter applies; the role scope (BR-2) resolved at enqueue time is passed verbatim
    /// (<paramref name="scopeKind"/> + <paramref name="scopeEmployeeId"/>) so the background file reproduces
    /// the requester's view exactly without needing the HTTP-bound ICurrentUser. On completion the requester
    /// (<paramref name="requestedByUserId"/>) is notified (in-app + email) that the export is ready (US-NTF-006 Phase 8).
    /// </summary>
    Task RunAsync(
        Guid tenantId,
        Guid reportId,
        Guid requestedByUserId,
        string scopeKind,
        Guid? scopeEmployeeId,
        LeaveReportType reportType,
        ReportExportFormat format,
        LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken);
}
