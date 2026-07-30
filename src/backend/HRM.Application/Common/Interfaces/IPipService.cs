using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Performance Improvement Plan service (US-PRF-008). Drives the workflow: HR creates a PIP (BR-1) with
/// objectives + checkpoint schedule + mentor + escalation (AC-1/AC-2), enforcing BR-2 (one non-terminal PIP per
/// employee) and BR-3 (≥30 days); the employee acknowledges (BR-4, mirroring the US-PRF-006 sign-off pattern);
/// the manager OR HR records append-only checkpoint assessments (AC-3/FR-4); HR sets the final outcome
/// (SuccessfullyCompleted | Extended | NotMet, AC-4) and confirms the escalation for a Not-Met PIP (AC-5/BR-6).
///
/// VISIBILITY (FR-8/BR-1): every read is restricted server-side to the employee, their manager, HR
/// (<c>Performance.Review.All</c>) and the assigned mentor. Managers may record checkpoints but CANNOT
/// close/extend a PIP (BR-1). Every operation is tenant-scoped via the EF global query filter (NFR-2). The full
/// immutable history (FR-5) lives in append-only checkpoint + <c>PipEvent</c> rows (no update/delete path).
/// </summary>
public interface IPipService
{
    /// <summary>
    /// Creates and initiates a PIP (AC-1/AC-2/FR-1). HR-only (BR-1). Enforces BR-2 (no second non-terminal PIP)
    /// and BR-3 (≥30 days). Resolves the manager from the employee's reporting line, writes the objectives +
    /// the immutable Created/Initiated events, notifies employee + manager + mentor, and schedules the Hangfire
    /// checkpoint reminders (FR-3). The PIP is created directly Active (BR-4 acknowledgement clock starts).
    /// </summary>
    Task<Result<PipDto>> CreateAsync(CreatePipInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the create-form pre-fill payload (AC-1). HR-only (same BR-1 gate as create). Resolves the target
    /// employee (name/job-title/manager) when an employeeId is supplied, a suggested reason from the flagged
    /// origin manager review (US-PRF-003) when a reviewId is supplied and it is that employee's review, the
    /// configured escalation actions (BR-6), and a BR-2 pre-check (already has a non-terminal PIP). A null
    /// employeeId returns a blank draft (escalation options only) for an HR-initiated form.
    /// </summary>
    Task<Result<PipDraftDto>> GetDraftAsync(GetPipDraftInput input, CancellationToken cancellationToken = default);

    /// <summary>Returns the PIP list for the dedicated PIP section (AC-1/BR-5), scoped to the caller's visibility (FR-8).</summary>
    Task<Result<IReadOnlyList<PipSummaryDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one full PIP (AC-1/FR-8). VISIBILITY-restricted: only the employee, their manager, HR or the
    /// assigned mentor — anyone else gets 403 (BR-1/FR-8). Includes the immutable checkpoint + event history.
    /// </summary>
    Task<Result<PipDto>> GetAsync(Guid pipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders one full PIP as a branded PDF (AC-1/FR-5). Reuses <see cref="GetAsync"/> for the data — so the
    /// SAME FR-8 visibility restriction + tenant query filter apply. Only <c>pdf</c> is supported; any other
    /// format fails 400 <c>invalid_format</c>.
    /// </summary>
    Task<Result<PerformanceExportFile>> ExportPdfAsync(
        Guid pipId, string? format, CancellationToken cancellationToken = default);

    /// <summary>
    /// Employee acknowledges PIP initiation (BR-4 — mirrors the US-PRF-006 acknowledgement). Caller must be the
    /// PIP's employee. Writes an IMMUTABLE Acknowledged event and flips the acknowledgement status.
    /// </summary>
    Task<Result<PipDto>> AcknowledgeAsync(AcknowledgePipInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an append-only checkpoint assessment (AC-3/FR-4). Manager (direct report) OR HR. Notifies the
    /// employee. The checkpoint row + a CheckpointRecorded event are immutable (FR-5) — no edit path.
    /// </summary>
    Task<Result<PipDto>> RecordCheckpointAsync(RecordCheckpointInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the final outcome (AC-4). HR-ONLY (BR-1 — a manager cannot close/extend). SuccessfullyCompleted →
    /// close + employee returns to normal; Extended → new end date + optional new objectives (FR-6); NotMet →
    /// triggers the configured escalation (pending HR confirmation, AC-5). Writes the matching immutable event.
    /// </summary>
    Task<Result<PipDto>> SetOutcomeAsync(SetPipOutcomeInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms the escalation decision for a Not-Met PIP (AC-5/BR-6). HR-ONLY. Records the escalation action +
    /// notes, notifies stakeholders, and writes an IMMUTABLE EscalationConfirmed audit event (FR-5).
    /// </summary>
    Task<Result<PipDto>> ConfirmEscalationAsync(ConfirmEscalationInput input, CancellationToken cancellationToken = default);
}
