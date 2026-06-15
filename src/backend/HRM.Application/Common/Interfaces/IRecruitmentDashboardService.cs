using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Recruitment dashboard + analytics service (US-REC-009). Read-only aggregation over the existing
/// recruitment data (Applicant, ApplicantStageHistory, Vacancy, Offer) — NO new entities/tables. All
/// reads run under the EF global query filter (TenantId == tenant) so the dashboard is tenant-isolated
/// (AC-5); there is no Postgres RLS (NFR-2 deferred, separate US-PLT-002 — same posture as the rest of
/// the module).
///
/// Produces: KPI cards (FR-1), the pipeline funnel + adjacent-stage conversion (FR-2/BR-3), source
/// effectiveness + per-source hire conversion (FR-3/BR-6), the time-to-hire trend bucketed by week or
/// month (FR-4), the vacancy-status summary (FR-5), and a capped recent-activity feed (FR-9). The date
/// range narrows the period metrics (FR-6); optional department / vacancy filters drill in (FR-7).
///
/// DEFERRALS (documented, do NOT block): PDF export (FR-8 — CSV + XLSX only here, mirroring the existing
/// ClosedXML seam; QuestPDF is available but the story's "full dashboard view" PDF is deferred), async
/// Hangfire export for large datasets (NFR-5), Redis pre-aggregation (NFR-3), and the materialized view
/// mv_recruitment_analytics (NFR-3). The dashboard is computed live from the DB on each request.
/// </summary>
public interface IRecruitmentDashboardService
{
    /// <summary>AC-1..AC-4/FR-1..FR-5/FR-9: the full dashboard payload for the filter.</summary>
    Task<Result<RecruitmentDashboardDto>> GetDashboardAsync(
        RecruitmentDashboardFilter filter, CancellationToken cancellationToken = default);

    /// <summary>FR-8: renders the dashboard's tabular data (KPIs + funnel + sources) as a CSV/XLSX download.</summary>
    Task<Result<RecruitmentDashboardExportResult>> ExportDashboardAsync(
        RecruitmentDashboardFilter filter, string format, CancellationToken cancellationToken = default);
}
