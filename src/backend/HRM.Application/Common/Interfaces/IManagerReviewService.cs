using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Manager performance-review service (US-PRF-003). A manager rates each of their DIRECT REPORTS against
/// their goals after the employee self-assessment, computes the weighted manager score and the FINAL
/// combined score (BR-4), and locks the review on submit (AC-5/BR-1). Every operation is tenant-scoped via
/// the EF global query filter (NFR-2); the direct-report scope (BR-2) and the HR-override / reopen
/// (BR-3/AC-5) are enforced here so the rules hold for any entry point.
/// </summary>
public interface IManagerReviewService
{
    /// <summary>
    /// Returns the manager's review workspace for an employee + cycle (AC-1): each goal WITH the employee's
    /// self-rating/comment alongside the manager's (possibly empty) rating/comment, plus the cycle's
    /// rating-scale + self/manager weights and the open/locked flags. The caller must be the employee's
    /// direct manager (Review.Team) or hold Review.All (HR).
    /// </summary>
    Task<Result<ManagerReviewDto>> GetReviewWorkspaceAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists partial manager-review progress without submitting. Validates field ranges (rating within
    /// the cycle scale) but NOT completeness. Fails 409 if the window is closed (BR-1) or the review is
    /// already submitted/locked (AC-5) — unless an HR caller (Review.All) reopens it implicitly.
    /// </summary>
    Task<Result<ManagerReviewDto>> SaveDraftAsync(
        SaveManagerReviewInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits the manager review (AC-2). Requires ALL assigned goals rated with a comment ≥20 chars
    /// (AC-3/FR-3 — returns the list of unrated goals on failure), computes the weighted manager score and
    /// the FINAL combined score (BR-4), flips status→Submitted, locks the record and notifies the employee.
    /// Fails 409 if the window is closed (BR-1) or already submitted (AC-5).
    /// </summary>
    Task<Result<ManagerReviewDto>> SubmitAsync(
        SaveManagerReviewInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens a submitted manager review for editing (AC-5/BR-3). HR-only (Review.All): flips status back
    /// to Draft so the manager (or HR) can edit. The window must still be open.
    /// </summary>
    Task<Result<ManagerReviewDto>> ReopenAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the team-reviews dashboard for the calling manager + cycle (AC-4): each direct report with
    /// their review status (pending self-assessment / self-assessment submitted / manager review pending /
    /// completed) and the available scores.
    /// </summary>
    Task<Result<TeamReviewsDashboardDto>> GetTeamReviewsAsync(
        Guid cycleId, CancellationToken cancellationToken = default);
}
