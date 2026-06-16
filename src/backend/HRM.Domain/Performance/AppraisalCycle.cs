using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// A performance appraisal cycle within a tenant.
///
/// MINIMAL placeholder created by US-PRF-001 to unblock goal-setting: it carries only what goal-setting
/// needs — a name, the goal-setting window (start/end), and a status. Full cycle management
/// (phases beyond goal-setting, review/calibration windows, lifecycle transitions, the create/edit UI)
/// is owned by US-PRF-004 and will flesh this entity out later. Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>.
/// Maps to the "appraisal_cycle" table.
/// </summary>
public sealed class AppraisalCycle : BaseEntity
{
    /// <summary>Human-readable cycle name, e.g. "FY2026 Annual Review" (required, max 200 chars).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Current lifecycle status. Defaults to Draft.</summary>
    public AppraisalCycleStatus Status { get; set; } = AppraisalCycleStatus.Draft;

    /// <summary>
    /// UTC start of the goal-setting window (BR-1/AC-5). Goals may only be created/edited/deleted while
    /// "now" is within [GoalSettingStart, GoalSettingEnd] inclusive.
    /// </summary>
    public DateTime GoalSettingStart { get; set; }

    /// <summary>UTC end of the goal-setting window (BR-1/AC-5).</summary>
    public DateTime GoalSettingEnd { get; set; }

    // ── Self-assessment window + scoring config (US-PRF-002) ───────────
    // MINIMAL extension to unblock US-PRF-002. Full cycle/phase management stays with US-PRF-004.

    /// <summary>
    /// UTC start of the employee self-assessment window (US-PRF-002 BR-1/AC-4). Self-assessments may
    /// only be saved/submitted while "now" is within [SelfAssessmentStart, SelfAssessmentEnd] inclusive.
    /// </summary>
    public DateTime SelfAssessmentStart { get; set; }

    /// <summary>UTC end of the self-assessment window (US-PRF-002 BR-1/AC-4). Also the reminder deadline (FR-7/AC-5).</summary>
    public DateTime SelfAssessmentEnd { get; set; }

    /// <summary>
    /// Tenant-configurable rating scale maximum (US-PRF-002 FR-2/BR-4). A self-rating must be an integer in
    /// [1, RatingScaleMax]. Defaults to 5 (a 1-5 scale). Stored per cycle so the scale is consistent for all
    /// goals in the cycle (Assumptions §10).
    /// </summary>
    public int RatingScaleMax { get; set; } = 5;

    /// <summary>
    /// Tenant-configurable self-rating weight in the self-vs-manager blend (US-PRF-002 BR-4), as a whole
    /// percent in [0, 100]. The manager weight is (100 - SelfWeightPercent). Default 30 ⇒ a 30:70 self:manager
    /// ratio. Used by US-PRF-002 only to surface the self-weight to the FE; the final blended score is computed
    /// once manager ratings land (later story).
    /// </summary>
    public int SelfWeightPercent { get; set; } = 30;

    /// <summary>
    /// True if "now" falls inside the goal-setting window AND the cycle is Active (BR-1/AC-5).
    /// Used by the goal service to fail-closed when the window has closed.
    /// </summary>
    public bool IsGoalSettingOpen(DateTime nowUtc)
        => Status == AppraisalCycleStatus.Active
           && nowUtc >= GoalSettingStart
           && nowUtc <= GoalSettingEnd;

    /// <summary>
    /// True if "now" falls inside the self-assessment window AND the cycle is Active (US-PRF-002 BR-1/AC-4).
    /// Used by the self-assessment service to fail-closed (read-only) when the window has closed.
    /// </summary>
    public bool IsSelfAssessmentOpen(DateTime nowUtc)
        => Status == AppraisalCycleStatus.Active
           && nowUtc >= SelfAssessmentStart
           && nowUtc <= SelfAssessmentEnd;
}
