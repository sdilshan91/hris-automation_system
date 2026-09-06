using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// A single phase within an appraisal cycle (US-PRF-004 FR-1/FR-2). Phases are sequential, non-overlapping,
/// and fall within the cycle's overall window (BR-3). The GoalSetting / SelfAssessment / ManagerReview phases
/// are the canonical SOURCE OF TRUTH for the open/closed window gates that US-PRF-001/002/003 consult — the
/// cycle's legacy *Start/*End columns are kept in sync with the corresponding phases on every save, and
/// <see cref="AppraisalCycle.IsGoalSettingOpen"/> et al. prefer a loaded phase when present.
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>.
/// Maps to the "cycle_phase" table.
/// </summary>
public sealed class CyclePhase : BaseEntity
{
    /// <summary>The cycle this phase belongs to (FK, required).</summary>
    public Guid CycleId { get; set; }

    /// <summary>Which phase this is (GoalSetting / SelfAssessment / ManagerReview / Calibration / Publish).</summary>
    public CyclePhaseType PhaseType { get; set; }

    /// <summary>0-based sequence position within the cycle (drives the timeline ordering + sequencing checks).</summary>
    public int Sequence { get; set; }

    /// <summary>UTC start of the phase window (inclusive).</summary>
    public DateTime StartDate { get; set; }

    /// <summary>UTC end of the phase window (inclusive).</summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// F3 / GAP-021 AC-3 — when this phase was marked complete, or null while it is still open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The FIRST phase-level state in this model: before this, a <c>CyclePhase</c> carried only a type, a
    /// sequence and a window, so "is this phase finished?" had no answer and every consumer had to proxy for
    /// one. The proxies are what this replaces — <c>RecommendationService</c>'s BR-2 gate checked whether a
    /// manager review had been submitted and returned <c>calibration_incomplete</c>, an error code describing
    /// a check it did not perform.
    /// </para>
    /// <para>
    /// <b>Deliberately general, not a calibration-specific flag on <see cref="AppraisalCycle"/>.</b> The
    /// general form is what lets <c>CyclePhaseTransitionJob</c> and the cycle dashboard stop special-casing
    /// Calibration, which is the ISSUE-350 defect. A boolean named for one phase would have solved AC-3 and
    /// left the other two consumers still guessing.
    /// </para>
    /// <para>
    /// <b>Null is not "incomplete-and-uninteresting".</b> A phase with no <c>CompletedOn</c> is open; a phase
    /// that never existed is a different statement, which is why completion is read as
    /// <c>Phases.Any(p =&gt; p.PhaseType == X &amp;&amp; p.CompletedOn != null)</c> rather than from a count.
    /// </para>
    /// </remarks>
    public DateTime? CompletedOn { get; set; }

    /// <summary>
    /// F3 — who marked the phase complete. Nullable because rows predating this column have no answer, and
    /// inventing one would be a fabrication of the kind the SLA-uptime and P95 fields already refuse.
    /// </summary>
    public Guid? CompletedByUserId { get; set; }

    /// <summary>True once <see cref="CompletedOn"/> is set. The single fact every consumer should read.</summary>
    public bool IsComplete => CompletedOn is not null;

    /// <summary>Navigation back to the owning cycle.</summary>
    public AppraisalCycle? Cycle { get; set; }

    /// <summary>True if "now" is within [StartDate, EndDate] inclusive (phase-window gate, irrespective of cycle status).</summary>
    public bool ContainsInstant(DateTime nowUtc) => nowUtc >= StartDate && nowUtc <= EndDate;
}
