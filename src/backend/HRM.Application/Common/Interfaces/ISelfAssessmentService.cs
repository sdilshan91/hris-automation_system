using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Employee self-assessment service (US-PRF-002). Every operation is scoped to the CALLING employee's own
/// record (resolved from <c>ICurrentUser.UserId</c>) AND their tenant (EF global query filter), so an
/// employee can never read or write another employee's self-assessment (NFR-2). The self-assessment-window
/// gate (BR-1/AC-4), the all-goals-rated + comment-length submit rules (BR-2/FR-3), the weighted-score
/// calc (FR-4) and the submitted-lock (BR-3) are all enforced here so they hold for any entry point.
/// </summary>
public interface ISelfAssessmentService
{
    /// <summary>
    /// Returns the calling employee's self-assessment for the given cycle, materializing one item per
    /// assigned goal (creating none in the DB) and merging any saved draft ratings (AC-1). Includes the
    /// cycle's rating-scale + self-weight config and the open/closed + locked flags.
    /// </summary>
    Task<Result<SelfAssessmentDto>> GetMyAssessmentAsync(Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists partial self-assessment progress without submitting (AC-3/FR-6). Validates field ranges
    /// (rating within scale, achievement 0-100) but NOT completeness. Fails 409 if the window is closed
    /// (BR-1) or the assessment is already submitted/locked (BR-3).
    /// </summary>
    Task<Result<SelfAssessmentDto>> SaveDraftAsync(SaveSelfAssessmentInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits the self-assessment (AC-2). Requires ALL assigned goals rated with a comment ≥20 chars
    /// (BR-2/FR-3), computes the weighted self-score (FR-4), locks the record (BR-3) and notifies the
    /// manager (AC-2). Fails 409 if the window is closed (BR-1) or already submitted.
    /// </summary>
    Task<Result<SelfAssessmentDto>> SubmitAsync(SaveSelfAssessmentInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens a submitted self-assessment back to <c>Draft</c> so the employee can edit and re-submit
    /// (US-PRF-004 BR-3/AC-2 — BUG-059). Manager/HR-only: the caller must hold <c>Performance.Review.All</c>
    /// (HR override) OR <c>Performance.Review.Team</c> AND be the target employee's direct manager — an
    /// employee can never reopen their own. Allowed only while the cycle's self-assessment window is still
    /// open (409 otherwise). Writes a <c>SelfAssessment.Reopened</c> audit row (actor + reason).
    /// </summary>
    Task<Result<SelfAssessmentDto>> ReopenAsync(Guid employeeId, Guid cycleId, string? reason, CancellationToken cancellationToken = default);
}
