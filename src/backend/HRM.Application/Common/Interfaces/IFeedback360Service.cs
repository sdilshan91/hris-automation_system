using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// 360-degree feedback submission + aggregation service (US-PRF-005 AC-3/AC-4/FR-4/FR-6). A reviewer submits
/// their feedback (one per reviewee per cycle, BR-3); HR/manager views the aggregated results (per-competency
/// + per-category averages, the composite score FR-6, the anonymized individual entries). Anonymity is
/// captured at submit time (BR-5) and enforced in the result projection (NFR-3/FR-5 — reviewer identifiers
/// are omitted from the DTO when anonymity is on). Every read/write is tenant-scoped via the EF global query
/// filter (NFR-2).
/// </summary>
public interface IFeedback360Service
{
    /// <summary>
    /// Submits the calling reviewer's 360 feedback for a reviewee + cycle (AC-3). Validates the reviewer has
    /// a Pending assignment, the ratings are in the cycle scale, and BR-3 (no duplicate). Persists the
    /// feedback, captures the anonymity flag (BR-5), marks the reviewer Completed, and updates the tracker.
    /// </summary>
    Task<Result<Feedback360ResultEntryDto>> SubmitFeedbackAsync(
        SubmitFeedback360Input input, CancellationToken cancellationToken = default);

    /// <summary>
    /// BUG-244 #2: returns ONE reviewer's feedback form for a single assignment (FR-4 / AC-2 deep link). The
    /// cycle comes from the assignment (not the active cycle). RLS: only the assigned reviewer (the caller's
    /// employee == the assignment's reviewer) may load the form, else 403 <c>not_assigned</c>. Questions are
    /// projected from the reviewee's cycle goals; ratings/comments hydrate from the persisted feedback once
    /// submitted.
    /// </summary>
    Task<Result<FeedbackFormDto>> GetFeedbackFormAsync(
        Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// BUG-244: submit-by-assignment — the reviewer form's submit. Resolves cycle + reviewee + reviewer from the
    /// reviewer assignment and enforces the SAME RLS as <see cref="GetFeedbackFormAsync"/> (caller's
    /// employee must be the assignment's reviewer, else 403 <c>not_assigned</c>). Maps each answer's questionId to
    /// a goal rating (the exact inverse of the #2 form projection) and reuses <see cref="SubmitFeedbackAsync"/>'s
    /// guards (Is360Enabled, Pending assignment, BR-3 no-duplicate, rating-range). Returns the now-locked form.
    /// </summary>
    Task<Result<FeedbackFormDto>> SubmitFeedbackByAssignmentAsync(
        Guid assignmentId, IReadOnlyList<FeedbackAnswerInput> answers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the aggregated 360 results for a reviewee + cycle (AC-4/FR-6): composite score, per-competency
    /// and per-category averages, completion tracker, and individual entries (reviewer identity omitted when
    /// anonymity is on, NFR-3). HR/manager only. The BR-4 minimum-peer release gate is surfaced as a warning.
    /// </summary>
    Task<Result<Feedback360ResultsDto>> GetResultsAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the 360 summary report data for a reviewee + cycle (FR-7), suitable for PDF rendering. Same
    /// payload as <see cref="GetResultsAsync"/>; the dedicated route signals the export intent to the FE.
    /// </summary>
    Task<Result<Feedback360ResultsDto>> GetReportDataAsync(
        Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default);
}
