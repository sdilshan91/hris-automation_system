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

    /// <summary>
    /// True if "now" falls inside the goal-setting window AND the cycle is Active (BR-1/AC-5).
    /// Used by the goal service to fail-closed when the window has closed.
    /// </summary>
    public bool IsGoalSettingOpen(DateTime nowUtc)
        => Status == AppraisalCycleStatus.Active
           && nowUtc >= GoalSettingStart
           && nowUtc <= GoalSettingEnd;
}
