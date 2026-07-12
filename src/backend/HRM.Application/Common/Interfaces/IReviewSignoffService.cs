using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Performance-review meeting-notes &amp; digital sign-off service (US-PRF-006). Drives the workflow:
/// add notes (only after the manager review is submitted, BR-1) → request employee sign-off (AC-2) →
/// employee acknowledge-&amp;-sign (AC-3) | dispute (AC-3/FR-4/FR-5) → HR resolve (BR-4). Sign-off records are
/// IMMUTABLE and append-only (NFR-3/BR-5) — this interface exposes no update/delete of a recorded sign-off,
/// and a SignedOff review is locked. Every operation is tenant-scoped via the EF global query filter (NFR-2);
/// the rich-text notes are sanitized on write (FR-1, §10). Mirrors <see cref="IManagerReviewService"/>'s
/// authorization (Review.Team direct-manager / Review.All HR).
/// </summary>
public interface IReviewSignoffService
{
    /// <summary>
    /// Returns the meeting notes + sign-off state for a review (AC-1). When no notes exist yet, returns the
    /// pre-populated template defaults (FR-1/FR-2) so the editor opens populated. Manager (direct report) or HR.
    /// </summary>
    Task<Result<ReviewMeetingNotesDto>> GetNotesAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves/updates the meeting notes WITHOUT requesting sign-off (AC-1). Requires the manager review to be
    /// submitted (BR-1 — 409 otherwise) and not locked (BR-5). Rich text is sanitized on write (FR-1). Sets
    /// sign-off status to NotesAdded.
    /// </summary>
    Task<Result<ReviewMeetingNotesDto>> SaveNotesAsync(
        SaveMeetingNotesInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the notes and requests employee sign-off (AC-2): status → PendingEmployeeSignOff, records an
    /// immutable manager RequestedSignOff entry, starts the BR-3 auto-close clock and notifies the employee.
    /// Requires the manager review submitted (BR-1) and not already signed off/locked (BR-5).
    /// </summary>
    Task<Result<ReviewMeetingNotesDto>> RequestSignOffAsync(
        SaveMeetingNotesInput input, string? clientIpAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Employee acknowledges &amp; signs (AC-3/FR-4): records an IMMUTABLE Acknowledged sign-off (signer name +
    /// timestamp + IP, FR-3/FR-7), status → SignedOff, LOCKS the review (BR-5). Caller must be the reviewed
    /// employee. Fails 409 unless the review is PendingEmployeeSignOff.
    /// </summary>
    Task<Result<ReviewMeetingNotesDto>> AcknowledgeAsync(
        SignoffActionInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Employee disputes (AC-3/FR-4/FR-5): mandatory comments, records an IMMUTABLE Disputed sign-off, status →
    /// Disputed, notifies the manager + HR. Stays Disputed until HR resolves (BR-4). Caller must be the
    /// reviewed employee. Fails 409 unless PendingEmployeeSignOff.
    /// </summary>
    Task<Result<ReviewMeetingNotesDto>> DisputeAsync(
        SignoffActionInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// HR resolves a dispute (BR-4): confirm as-is or amend, recording an IMMUTABLE DisputeConfirmed /
    /// DisputeAmended sign-off. Confirm → SignedOff + lock; amend → back to NotesAdded so the manager can edit
    /// and re-request. HR-only (Review.All). Fails 409 unless the review is Disputed.
    /// </summary>
    Task<Result<ReviewMeetingNotesDto>> ResolveDisputeAsync(
        ResolveDisputeInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the complete review record for export (AC-4/FR-6): goals, ratings, notes, sign-off timestamps
    /// and the full signature log. Data only — PDF rendering is a deliberate seam (no PDF lib; see US-PRF-005).
    /// </summary>
    Task<Result<ReviewExportDto>> GetExportRecordAsync(
        Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default);

    // ── Caller-scoped self-service (ISSUE-288) ──────────────────────────
    // The employee self-view holds neither its employeeId nor a cycleId. These resolve BOTH server-side
    // (employeeId from the caller's Employee row, cycleId from the active appraisal cycle) and then reuse the
    // manager-side notes / acknowledge / dispute logic verbatim. 403 no_employee_record when the caller has no
    // Employee row; 404 no_active_cycle when none is active.

    /// <summary>
    /// Returns the caller's OWN meeting notes + sign-off state for the active cycle (US-PRF-006 AC-1/AC-3,
    /// ISSUE-288). Read.Self — the reviewed employee reading their own review; no manager/HR authz applies.
    /// </summary>
    Task<Result<ReviewMeetingNotesDto>> GetMyMeetingNotesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller acknowledges &amp; signs their own review for the active cycle (US-PRF-006 AC-3, ISSUE-288).
    /// Resolves employeeId + cycleId from the caller, then forwards to the immutable-signature acknowledge path.
    /// </summary>
    Task<Result<ReviewMeetingNotesDto>> AcknowledgeMyReviewAsync(
        string? clientIpAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller disputes their own review for the active cycle (US-PRF-006 AC-3/FR-4/FR-5, ISSUE-288).
    /// Resolves employeeId + cycleId from the caller, then forwards to the dispute path (comments mandatory).
    /// </summary>
    Task<Result<ReviewMeetingNotesDto>> DisputeMyReviewAsync(
        string? comments, string? clientIpAddress, CancellationToken cancellationToken = default);
}
