using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A structured evaluation an interviewer submits for an <see cref="Interview"/> (US-REC-006). One scorecard
/// per interviewer per interview (FR-2) — enforced by a unique index on
/// (tenant_id, interview_id, interviewer_employee_id). Tenant-scoped via <see cref="BaseEntity.TenantId"/>
/// and the EF global query filter + <c>TenantInterceptor</c> (this codebase enforces tenant isolation with
/// EF query filters, not Postgres RLS — RLS/NFR-2 is a separate deferred story; see the module note). Maps
/// to the "interview_scorecard" table.
///
/// Editable until <see cref="LockedAt"/> (interview time + a configurable lock period, BR-4); after that it
/// is immutable. <see cref="AverageScore"/> is computed from the criterion ratings on submit/edit (FR-3).
/// </summary>
public sealed class InterviewScorecard : BaseEntity, IAuditExempt
{
    /// <summary>FK to the evaluated <see cref="Interview"/> (FR-1, required). Plain UUID column.</summary>
    public Guid InterviewId { get; set; }

    /// <summary>
    /// FK to the submitting interviewer's <see cref="Employee"/> record (BR-1, required). Plain UUID column.
    /// The submitter must be an assigned interviewer on <see cref="InterviewId"/> (validated in the service).
    /// </summary>
    public Guid InterviewerEmployeeId { get; set; }

    /// <summary>The interviewer's overall hiring recommendation (FR-1/BR-3, mandatory).</summary>
    public OverallRecommendation OverallRecommendation { get; set; }

    /// <summary>
    /// Mean of the criterion ratings (FR-3), recomputed on every submit/edit. Rounded to 2 decimals.
    /// </summary>
    public decimal AverageScore { get; set; }

    /// <summary>Optional free-text overall notes for the recruiter (FR-1 general notes).</summary>
    public string? GeneralNotes { get; set; }

    /// <summary>UTC instant the scorecard was first submitted (§7 submitted_at).</summary>
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC instant after which the scorecard is immutable (BR-4): interview start + the lock period
    /// (default 48h, configurable). Computed and stored on first submit so the window is stable even if the
    /// interview is later rescheduled.
    /// </summary>
    public DateTime LockedAt { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    /// <summary>The per-criterion ratings making up this scorecard (FR-1). ≥1 required.</summary>
    public ICollection<ScorecardCriterionRating> Ratings { get; set; } = new List<ScorecardCriterionRating>();
}
