using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;


/// <summary>
/// A manager's performance review of one employee for one appraisal cycle (US-PRF-003). Exactly one per
/// employee per cycle (enforced by a tenant-scoped unique index). Holds the overall status, the weighted
/// manager score, the FINAL combined score (self+manager blend, BR-4), an overall summary comment (FR-5)
/// and an optional flag (FR-6). Per-goal manager ratings live in the child <see cref="ManagerReviewItem"/>
/// rows. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter +
/// <c>TenantInterceptor</c> (NFR-2). Maps to the "manager_review" table.
/// </summary>
public sealed class ManagerReview : BaseEntity, IAuditExempt
{
    /// <summary>The appraisal cycle this review belongs to (FK, required).</summary>
    public Guid CycleId { get; set; }

    /// <summary>The employee being reviewed (FK to <see cref="Employee"/>, required).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// The employee record of the reviewing manager (FK to <see cref="Employee"/>, set when first saved).
    /// Recorded for traceability; the authoritative manager user id + timestamp ride the audit fields (FR-7).
    /// </summary>
    public Guid? ReviewerEmployeeId { get; set; }

    /// <summary>Overall status. Defaults to Draft on first save (AC-2).</summary>
    public ManagerReviewStatus Status { get; set; } = ManagerReviewStatus.Draft;

    /// <summary>
    /// Weighted manager score (AC-2), computed at submit from each item's managerRating and its goal's
    /// weight, on the cycle's rating scale: Σ(rating*weight)/Σ(weight). Null until submitted.
    /// </summary>
    public decimal? WeightedManagerScore { get; set; }

    /// <summary>
    /// Final combined score (BR-4): (self_score * self_weight) + (manager_score * manager_weight) using the
    /// cycle's tenant-configured ratio. Null until submitted. US-PRF-005 will extend the blend to also weight
    /// peer/report (360) ratings — see <c>ManagerReviewService.ComputeFinalScore</c>.
    /// </summary>
    public decimal? FinalScore { get; set; }

    /// <summary>
    /// The self-score this final blend was computed against (the submitting employee's WeightedSelfScore, or
    /// 0 if no self-assessment was submitted). Stored for transparency/recompute. Null until submitted.
    /// </summary>
    public decimal? SelfScoreAtSubmit { get; set; }

    /// <summary>Overall manager summary comment (FR-5, optional, max 5000 chars).</summary>
    public string? SummaryComment { get; set; }

    /// <summary>Optional disposition flag — Recognition / Promotion / PIP (FR-6). None when unset.</summary>
    public ReviewFlag Flag { get; set; } = ReviewFlag.None;

    /// <summary>UTC submission timestamp (AC-2). Null until submitted.</summary>
    public DateTime? SubmittedAt { get; set; }

    // ── Meeting-notes & sign-off workflow (US-PRF-006) ──────────────────

    /// <summary>
    /// Sign-off lifecycle (US-PRF-006). Starts at <see cref="ReviewSignoffStatus.NotStarted"/> and only
    /// advances once the review is <see cref="ManagerReviewStatus.Submitted"/> (BR-1). The meeting-notes +
    /// digital-sign-off workflow is intentionally separate from the rating <see cref="Status"/>.
    /// </summary>
    public ReviewSignoffStatus SignoffStatus { get; set; } = ReviewSignoffStatus.NotStarted;

    /// <summary>
    /// UTC timestamp employee sign-off was requested (AC-2). Drives the BR-3 auto-close clock — a review still
    /// <see cref="ReviewSignoffStatus.PendingEmployeeSignOff"/> this many days later is auto-closed.
    /// </summary>
    public DateTime? SignoffRequestedAt { get; set; }

    /// <summary>UTC timestamp the review reached a terminal sign-off state (SignedOff / NoResponse). Null otherwise.</summary>
    public DateTime? SignoffCompletedAt { get; set; }

    /// <summary>
    /// True once the review is locked against edits (BR-5) — set when the employee signs off
    /// (<see cref="ReviewSignoffStatus.SignedOff"/>). A locked review's notes and sign-off cannot be changed.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Optimistic concurrency token (NFR-3 — prevents lost updates if HR and the manager edit at once).
    /// Maps to the PostgreSQL xmin system column via the Npgsql row-version convention (no schema DDL);
    /// the InMemory test provider ignores it. Mirrors <c>Goal.Version</c> / <c>SelfAssessment.Version</c>.
    /// </summary>
    public uint Version { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public AppraisalCycle? Cycle { get; set; }
    public Employee? Employee { get; set; }
    public List<ManagerReviewItem> Items { get; set; } = [];

    /// <summary>The meeting notes for this review (US-PRF-006), at most one.</summary>
    public ReviewMeetingNotes? MeetingNotes { get; set; }

    /// <summary>The immutable, append-only sign-off log (US-PRF-006 NFR-3/BR-5).</summary>
    public List<ReviewSignoff> Signoffs { get; set; } = [];
}
