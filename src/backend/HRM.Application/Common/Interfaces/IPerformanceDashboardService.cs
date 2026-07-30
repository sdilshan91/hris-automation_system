using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Performance dashboard + analytics service (US-PRF-007). Read-only aggregation over the existing
/// performance data (ManagerReview, SelfAssessment, Goal, Feedback360, AppraisalCycle, CyclePhase,
/// CycleParticipant) joined with Core HR (Employee, Department, JobTitle, Location) — NO new entities/tables.
/// All reads run under the EF global query filter (TenantId == tenant) so every aggregate is tenant-isolated
/// (NFR-2/AC-5); there is no Postgres RLS layer (deferred to the platform US-PLT-002, same posture as the
/// other module dashboards). The headline final score reuses ManagerReview.FinalScore (BR-4) — this service
/// never recomputes scores.
///
/// SCOPE (BR-1/BR-3/AC-5): callers with Performance.View.All get org-wide aggregates plus top/bottom
/// performer lists; callers with only Performance.View.Team get aggregates over their DIRECT REPORTS and a
/// team ranking instead of org-wide top/bottom. The service resolves the scope from the caller's permissions;
/// a manager can NEVER pull org-wide data through these methods.
///
/// EXTENSION POINT (NFR-3/BR-4): the platform does not yet use Postgres materialized views or a Redis cache
/// for these aggregates. They are computed live with tenant-scoped EF GroupBy queries on each request (correct
/// and reasonably efficient for the current data sizes). A future story can add a performance_summary
/// materialized view + a Hangfire 4-hourly refresh + a Redis read-through cache WITHOUT changing this contract
/// — the DTOs are the stable seam.
///
/// DEFERRALS (documented, do NOT block): PDF export (FR-8 — CSV + XLSX only here; QuestPDF is available but
/// commented out in Infrastructure, mirroring the recruitment-dashboard seam) and async large-dataset export.
/// </summary>
public interface IPerformanceDashboardService
{
    /// <summary>AC-1/AC-2/FR-1..FR-3/FR-6: the full dashboard overview for the filter, scoped to the caller.</summary>
    Task<Result<PerformanceDashboardDto>> GetOverviewAsync(
        PerformanceDashboardFilter filter, CancellationToken cancellationToken = default);

    /// <summary>FR-5/AC-2: drill-down — a department's employees + individual scores for the cycle.</summary>
    Task<Result<DepartmentDrilldownDto>> GetDepartmentDrilldownAsync(
        Guid departmentId, PerformanceDashboardFilter filter, CancellationToken cancellationToken = default);

    /// <summary>AC-3/FR-7: multi-cycle average-score trend, optionally with a per-department overlay.</summary>
    Task<Result<PerformanceTrendDto>> GetTrendAsync(
        IReadOnlyList<Guid> cycleIds, PerformanceDashboardFilter filter, bool includeDepartmentSeries,
        CancellationToken cancellationToken = default);

    /// <summary>FR-8/AC-4: renders the overview's tabular data as a CSV/XLSX download (PDF deferred).</summary>
    Task<Result<PerformanceDashboardExportResult>> ExportOverviewAsync(
        PerformanceDashboardFilter filter, string format, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-PRF-011 §2: the calibration cohort for a cycle — each in-scope employee's ORIGINAL rating, current
    /// CALIBRATED rating (if any), reviewer and department. Reuses the same scope + population + filter logic
    /// as the dashboard (LoadPopulationAsync), so it honours the manager-vs-HR scope and the FR-4 filters.
    /// </summary>
    Task<Result<CalibrationCohortDto>> GetCalibrationCohortAsync(
        PerformanceDashboardFilter filter, CancellationToken cancellationToken = default);
}
