using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// One calibration action on a manager review's overall rating (US-PRF-011). A calibration NEVER mutates the
/// review's own <see cref="ManagerReview.FinalScore"/> (the ORIGINAL rating) — instead each adjustment is
/// recorded as an APPEND-ONLY history row here, capturing who changed it, when, why, and the value it moved
/// from/to. This preserves a full audit trail and supports repeated calibration rounds; the CURRENT calibrated
/// value for an employee is the most-recent row (by <see cref="BaseEntity.CreatedAt"/>).
///
/// <para>Model-shape decision (US-PRF-011 §1): a SEPARATE table (not extra columns on <c>manager_review</c>)
/// because (a) the original rating must be provably immutable — it lives on a row this table never writes;
/// (b) calibration is multi-round; and (c) this is compensation-adjacent data where "who changed my rating and
/// why" is an audited question. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query
/// filter + <c>TenantInterceptor</c> (NFR-2), plus a DORMANT Postgres RLS <c>tenant_isolation</c> policy shipped
/// in the creating migration. Maps to the "rating_calibration" table.</para>
/// </summary>
public sealed class RatingCalibration : BaseEntity
{
    /// <summary>The appraisal cycle this calibration belongs to (FK, required).</summary>
    public Guid CycleId { get; set; }

    /// <summary>The employee whose rating is being calibrated (FK to <see cref="Employee"/>, required).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>The manager review whose final score is being calibrated (FK, required).</summary>
    public Guid ManagerReviewId { get; set; }

    /// <summary>
    /// Snapshot of the review's ORIGINAL final score at the time of this calibration — i.e.
    /// <see cref="ManagerReview.FinalScore"/>, which this table never writes. Identical across every round for
    /// one review, so the original is recoverable even from a single history row.
    /// </summary>
    public decimal OriginalScore { get; set; }

    /// <summary>
    /// The calibrated value in effect BEFORE this action (the prior round's <see cref="CalibratedScore"/>), or
    /// null on the first calibration round. Records the delta chain for the audit trail.
    /// </summary>
    public decimal? PreviousCalibratedScore { get; set; }

    /// <summary>The new calibrated overall score set by this action (on the cycle's rating scale).</summary>
    public decimal CalibratedScore { get; set; }

    /// <summary>Mandatory justification for the calibration (why the rating was adjusted). Max 2000 chars.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>The user (HR calibrator) who applied this calibration (FR — who changed it).</summary>
    public Guid CalibratedByUserId { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public AppraisalCycle? Cycle { get; set; }
    public ManagerReview? ManagerReview { get; set; }
}
