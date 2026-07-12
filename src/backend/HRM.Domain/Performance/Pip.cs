using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// A Performance Improvement Plan for an underperforming employee (US-PRF-008). HR-only created (BR-1); an
/// employee may have only ONE non-terminal PIP at a time (BR-2, enforced by a tenant-scoped partial unique
/// index + a service check). Carries the reason, duration (≥30 days, BR-3), assigned mentor, configured
/// escalation action (BR-6), final outcome, and the employee acknowledgement (BR-4 — the immutable acknowledge
/// + the "Not Acknowledged" flag if not acknowledged within 5 business days). Objectives and checkpoints live in
/// the child <see cref="PipObjective"/> / <see cref="PipCheckpoint"/> rows; the full immutable action history
/// (FR-5) lives in the append-only <see cref="PipEvent"/> log, mirroring the US-PRF-006 ReviewSignoff pattern.
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>
/// (NFR-2). Maps to the "pip" table.
///
/// ENCRYPTION (NFR-4): the sensitive free-text fields (<see cref="Reason"/>, <see cref="EscalationNotes"/>,
/// <see cref="FinalOutcomeNotes"/>) are ENCRYPTED AT REST (P3-4) via the app-side AES-256-GCM
/// <c>IFieldEncryptor</c>, applied as an EF value converter in <c>PipConfiguration.ApplyEncryption</c> — the
/// columns are stored as <c>enc:v1:</c> ciphertext (<c>text</c>) and transparently decrypt on read.
/// </summary>
public sealed class Pip : BaseEntity
{
    /// <summary>The employee under the improvement plan (FK to <see cref="Employee"/>, required).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// The manager who owns the employee at PIP creation (FK to <see cref="Employee"/>). Recorded for
    /// notification + visibility (FR-8/BR-1). Resolved from the employee's <c>ReportsToEmployeeId</c>.
    /// </summary>
    public Guid? ManagerEmployeeId { get; set; }

    /// <summary>The assigned mentor/coach (FK to <see cref="Employee"/>, AC-1/FR-1). Can view the PIP (FR-8).</summary>
    public Guid? MentorEmployeeId { get; set; }

    /// <summary>
    /// The optional manager review this PIP was initiated from (US-PRF-003 flagged review, FK to
    /// <see cref="ManagerReview"/>). Null when HR initiated the PIP independently.
    /// </summary>
    public Guid? OriginManagerReviewId { get; set; }

    /// <summary>SENSITIVE (NFR-4 — encrypted at rest, P3-4). The reason for the PIP (free text, FR-1).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>PIP start date (FR-1).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>PIP end date. Must be ≥30 days after the start date (BR-3, FR-1).</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// The planned checkpoint review dates (AC-1/FR-1). Drives the FR-3 checkpoint reminders (3 days before each)
    /// and overdue-checkpoint alerts dispatched by the daily <c>IPipReminderService</c> sweep. Stored as a
    /// PostgreSQL date[] column; the recorded assessments themselves are the separate <see cref="PipCheckpoint"/>
    /// rows.
    /// </summary>
    public List<DateOnly> ScheduledCheckpointDates { get; set; } = [];

    /// <summary>Lifecycle status (FR-2). Defaults to Draft on first save.</summary>
    public PipStatus Status { get; set; } = PipStatus.Draft;

    /// <summary>The escalation action to take if the PIP is Not Met (BR-6/AC-1/AC-5).</summary>
    public PipEscalationAction EscalationAction { get; set; } = PipEscalationAction.None;

    /// <summary>UTC timestamp the PIP was initiated (Draft → Active, AC-2). Null while Draft.</summary>
    public DateTime? InitiatedAt { get; set; }

    // ── Acknowledgement (BR-4, mirrors the US-PRF-006 sign-off pattern) ──

    /// <summary>Employee acknowledgement state (BR-4). Pending once Active; the Hangfire job flips to NotAcknowledged.</summary>
    public PipAcknowledgementStatus AcknowledgementStatus { get; set; } = PipAcknowledgementStatus.Pending;

    /// <summary>UTC timestamp the employee acknowledged the PIP (BR-4). Null until acknowledged.</summary>
    public DateTime? AcknowledgedAt { get; set; }

    // ── Final outcome (AC-4/AC-5) ───────────────────────────────────────

    /// <summary>UTC timestamp the final outcome was set (AC-4). Null until SuccessfullyCompleted/NotMet.</summary>
    public DateTime? OutcomeSetAt { get; set; }

    /// <summary>SENSITIVE (NFR-4 — encrypted at rest, P3-4). Optional free-text notes recorded with the final outcome (AC-4).</summary>
    public string? FinalOutcomeNotes { get; set; }

    /// <summary>True once HR has confirmed the escalation for a Not-Met PIP (AC-5/BR-6).</summary>
    public bool EscalationConfirmed { get; set; }

    /// <summary>UTC timestamp the escalation decision was confirmed (AC-5). Null otherwise.</summary>
    public DateTime? EscalationConfirmedAt { get; set; }

    /// <summary>SENSITIVE (NFR-4 — encrypted at rest, P3-4). HR's escalation decision notes (AC-5).</summary>
    public string? EscalationNotes { get; set; }

    /// <summary>
    /// Optimistic concurrency token (NFR — prevents lost updates). Maps to the PostgreSQL xmin system column via
    /// the Npgsql row-version convention (no schema DDL); the InMemory test provider ignores it. Mirrors
    /// <c>ManagerReview.Version</c>.
    /// </summary>
    public uint Version { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────
    public Employee? Employee { get; set; }

    /// <summary>The improvement objectives (AC-1/FR-1).</summary>
    public List<PipObjective> Objectives { get; set; } = [];

    /// <summary>The recorded checkpoint assessments — append-only history (AC-3/FR-4/FR-5).</summary>
    public List<PipCheckpoint> Checkpoints { get; set; } = [];

    /// <summary>The immutable, append-only action log (FR-5 — acknowledgement, outcome, escalation, status changes).</summary>
    public List<PipEvent> Events { get; set; } = [];
}
