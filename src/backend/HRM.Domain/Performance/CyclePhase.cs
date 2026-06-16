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

    /// <summary>Navigation back to the owning cycle.</summary>
    public AppraisalCycle? Cycle { get; set; }

    /// <summary>True if "now" is within [StartDate, EndDate] inclusive (phase-window gate, irrespective of cycle status).</summary>
    public bool ContainsInstant(DateTime nowUtc) => nowUtc >= StartDate && nowUtc <= EndDate;
}
