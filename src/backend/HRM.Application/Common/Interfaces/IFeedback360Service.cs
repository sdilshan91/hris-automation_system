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

    /// <summary>
    /// Releases a reviewee's aggregated 360 results to them for a cycle (BR-4/FR-3). HR (Review.All) or the
    /// reviewee's own reporting manager (Review.Team). HARD-BLOCKS below the cycle's minimum peer threshold
    /// (422 <c>min_peer_threshold_not_met</c>); at/above the minimum it succeeds. Idempotent — a re-release
    /// returns the existing row (200) and never writes a second. Notifies the reviewee (and their manager).
    /// </summary>
    Task<Result<Feedback360ReleaseDto>> ReleaseResultsAsync(
        Guid cycleId, Guid revieweeEmployeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the CALLER's OWN aggregated 360 results for a cycle (FR-3/FR-5). Self-scoped (no permission gate):
    /// resolves the caller's employee, 404 <c>not_released</c> until a release row exists, and returns the SAME
    /// aggregation as HR but with every reviewer identity stripped unconditionally (FR-5) and the PDF export
    /// marked unavailable.
    /// </summary>
    Task<Result<Feedback360ResultsDto>> GetMyResultsAsync(
        Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders the 360 summary report as a branded PDF (FR-7). Reuses <see cref="GetReportDataAsync"/> for the
    /// data — so the SAME HR-only authorization + tenant query filter apply and anonymity (NFR-3/FR-5) is already
    /// enforced in the projection. Only <c>pdf</c> is supported (there is no CSV/XLSX for this report); any other
    /// format fails 400 <c>invalid_format</c>.
    /// </summary>
    Task<Result<PerformanceExportFile>> ExportReportAsync(
        Guid revieweeEmployeeId, Guid cycleId, string? format, CancellationToken cancellationToken = default);
}
