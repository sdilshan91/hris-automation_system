using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Performance-based recommendation service (US-PRF-010). Drives the workflow: HR opens the recommendation
/// workspace for a completed cycle (AC-1), optionally auto-generates SUGGESTIONS from the tenant's rating-threshold
/// rules (AC-2/FR-2/BR-3), manually creates/overrides recommendations with a mandatory justification on override
/// (FR-3), submits them through the approval workflow (AC-3/FR-4, gated by BR-1 published final ratings + BR-2
/// calibration), approvers act (FR-4), and HR reviews aggregate stats (AC-4) + budget tracking (FR-8/BR-4).
///
/// ACCESS (AC-5/NFR-5/BR access): full workspace + generate + submit + budget/rule config require
/// <c>Performance.Publish.All</c> (HR). A manager (<c>Performance.Review.Team</c>) sees ONLY their direct reports'
/// recommendations + status (no other teams, no general employee access) — enforced server-side. Every read/write
/// is tenant-scoped via the EF global query filter (NFR-2).
///
/// DOWNSTREAM (BR-6): on final approval a clearly-marked integration event is raised via
/// <see cref="IRecommendationIntegrationService"/> — promotions→Core HR, bonuses→Payroll, training→Training. The
/// real cross-module wiring is deferred (a documented seam, not built here).
///
/// ENCRYPTION (NFR-3): compensation fields are sensitive financial PII stored as plain numeric today — there is no
/// field/PII encryption mechanism in the codebase (same decision as US-PRF-008). A documented seam, not built here.
/// </summary>
public interface IRecommendationService
{
    /// <summary>
    /// The recommendation workspace for a completed cycle (AC-1): in-scope employees with their final score,
    /// current grade/title/compensation, tenure, manager flag and existing recommendation. Paginated (NFR-4). HR
    /// sees all employees; a manager sees only their direct reports (AC-5).
    /// </summary>
    Task<Result<RecommendationWorkspaceDto>> GetWorkspaceAsync(
        RecommendationWorkspaceQueryInput input, CancellationToken cancellationToken = default);

    /// <summary>Returns one full recommendation (AC-5 scope-restricted — HR or the employee's manager).</summary>
    Task<Result<RecommendationDto>> GetAsync(Guid recommendationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Auto-generates recommendation SUGGESTIONS across the cycle from the tenant's active rating-threshold rules
    /// (AC-2/FR-2/BR-3). Each in-scope employee with a submitted final score is matched against the rules; the
    /// highest-threshold match seeds a Draft suggestion (skipping employees who already have a recommendation).
    /// HR-only (Publish.All). Suggestions only — HR reviews + submits (BR-3).
    /// </summary>
    Task<Result<AutoGenerateResultDto>> AutoGenerateAsync(Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually creates OR overrides a recommendation for an employee in a cycle (FR-1/FR-3/BR-5). HR-only. A
    /// JUSTIFICATION is MANDATORY when overriding an existing (especially auto-generated) recommendation (FR-3); a
    /// promotion requires a target grade + effective date (BR-5). Charges the budget when applicable (FR-8).
    /// </summary>
    Task<Result<RecommendationDto>> SaveAsync(SaveRecommendationInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a recommendation into the approval workflow (AC-3/FR-4). HR-only. Links it to the employee's
    /// performance record (the cycle's submitted ManagerReview), builds the ordered approver chain and sets the
    /// status to PendingApproval (or Submitted when no approvers). Gated by BR-1 (cycle final ratings published)
    /// and BR-2 (calibration complete if enabled). Charges the budget and surfaces the BR-4 soft warning.
    /// </summary>
    Task<Result<RecommendationDto>> SubmitAsync(SubmitRecommendationInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// An approver approves or rejects their step (FR-4). The caller must be the current pending approver. On the
    /// last approval the recommendation is Approved and the downstream-integration seam is raised (BR-6); any
    /// rejection terminates it as Rejected and reverses the budget charge.
    /// </summary>
    Task<Result<RecommendationDto>> DecideAsync(DecideRecommendationInput input, CancellationToken cancellationToken = default);

    /// <summary>Aggregate recommendation summary stats for the cycle (AC-4/FR-6): promotions, bonus pool,
    /// increment distribution by department, prior-cycle comparison + budget. HR-only.</summary>
    Task<Result<RecommendationSummaryDto>> GetSummaryAsync(Guid? cycleId, CancellationToken cancellationToken = default);

    /// <summary>Exports the recommendation summary as a CSV/XLSX file (FR-6, ClosedXML — PDF deferred). HR-only.</summary>
    Task<Result<RecommendationExportResult>> ExportSummaryAsync(
        Guid? cycleId, string format, CancellationToken cancellationToken = default);

    // ── Budget config (FR-8) ────────────────────────────────────────────

    /// <summary>Creates or updates the cycle's bonus/increment budget (FR-8). HR-only. Idempotent per cycle.</summary>
    Task<Result<RecommendationBudgetDto>> SaveBudgetAsync(SaveBudgetInput input, CancellationToken cancellationToken = default);

    /// <summary>Returns the cycle's budget tracker (FR-8/BR-4), or null when none is configured. HR-only.</summary>
    Task<Result<RecommendationBudgetDto?>> GetBudgetAsync(Guid cycleId, CancellationToken cancellationToken = default);

    // ── Rule config (FR-2) ──────────────────────────────────────────────

    /// <summary>Lists the tenant's auto-generation rules (FR-2). HR-only.</summary>
    Task<Result<IReadOnlyList<RecommendationRuleDto>>> ListRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates or updates an auto-generation rule (FR-2). HR-only.</summary>
    Task<Result<RecommendationRuleDto>> SaveRuleAsync(SaveRuleInput input, CancellationToken cancellationToken = default);
}
