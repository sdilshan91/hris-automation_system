using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// A performance appraisal cycle within a tenant.
///
/// Originally a MINIMAL placeholder (US-PRF-001..003) carrying a name, the goal-setting / self-assessment /
/// manager-review windows, status, and the rating-scale + self:manager weight config. US-PRF-004 turns it
/// into FULL cycle management: cycle type, an overall window, the full status state machine, a sequence of
/// <see cref="CyclePhase"/> rows that are the SOURCE OF TRUTH for the window gates, a resolved set of
/// <see cref="CycleParticipant"/> rows, and feature toggles (360, calibration, anonymity).
///
/// Backward-compat: the legacy <c>GoalSettingStart/End</c>, <c>SelfAssessmentStart/End</c>,
/// <c>ManagerReviewStart/End</c> columns are RETAINED and kept in sync with the corresponding phases on save,
/// so US-PRF-001/002/003 services (which read those columns / call the IsXOpen helpers) keep working unchanged.
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>.
/// Maps to the "appraisal_cycle" table.
/// </summary>
public sealed class AppraisalCycle : BaseEntity
{
    /// <summary>Human-readable cycle name, e.g. "FY2026 Annual Review" (required, max 200 chars).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Cycle type — Annual / Quarterly / Probation (US-PRF-004 FR-4). Drives BR-4. Defaults to Annual.</summary>
    public CycleType Type { get; set; } = CycleType.Annual;

    /// <summary>Current lifecycle status. Defaults to Draft.</summary>
    public AppraisalCycleStatus Status { get; set; } = AppraisalCycleStatus.Draft;

    // ── Overall cycle window (US-PRF-004 BR-3) ─────────────────────────

    /// <summary>UTC start of the overall cycle (US-PRF-004 §7). All phases must fall within [StartDate, EndDate].</summary>
    public DateTime StartDate { get; set; }

    /// <summary>UTC end of the overall cycle (US-PRF-004 §7).</summary>
    public DateTime EndDate { get; set; }

    // ── Legacy phase windows (US-PRF-001/002/003) — kept in sync with phases ───

    /// <summary>
    /// UTC start of the goal-setting window (BR-1/AC-5). Retained for back-compat; kept in sync with the
    /// GoalSetting <see cref="CyclePhase"/> on save. Goals may only be created/edited while open.
    /// </summary>
    public DateTime GoalSettingStart { get; set; }

    /// <summary>UTC end of the goal-setting window (BR-1/AC-5). Synced from the GoalSetting phase.</summary>
    public DateTime GoalSettingEnd { get; set; }

    /// <summary>UTC start of the self-assessment window (US-PRF-002). Synced from the SelfAssessment phase.</summary>
    public DateTime SelfAssessmentStart { get; set; }

    /// <summary>UTC end of the self-assessment window (US-PRF-002). Synced from the SelfAssessment phase.</summary>
    public DateTime SelfAssessmentEnd { get; set; }

    /// <summary>UTC start of the manager-review window (US-PRF-003). Synced from the ManagerReview phase.</summary>
    public DateTime ManagerReviewStart { get; set; }

    /// <summary>UTC end of the manager-review window (US-PRF-003). Synced from the ManagerReview phase.</summary>
    public DateTime ManagerReviewEnd { get; set; }

    // ── Rating-scale / weight config (US-PRF-002/003) ──────────────────

    /// <summary>Tenant-configurable rating scale maximum (US-PRF-002 FR-2/BR-4). Locked once Active (BR-5). Default 5.</summary>
    public int RatingScaleMax { get; set; } = 5;

    /// <summary>Self-rating weight in the self-vs-manager blend (US-PRF-002 BR-4), 0-100. Manager weight is 100-this. Default 30.</summary>
    public int SelfWeightPercent { get; set; } = 30;

    // ── Sign-off config (US-PRF-006) ───────────────────────────────────

    /// <summary>
    /// Tenant-configurable number of days an employee has to sign off a review before it auto-closes with
    /// <c>NoResponse</c> status (US-PRF-006 BR-3, §10). Default 7.
    /// </summary>
    public int SignoffAutoCloseDays { get; set; } = 7;

    /// <summary>
    /// Tenant-configurable default meeting-notes template (US-PRF-006 FR-1, §10 — HR-configured at the tenant
    /// level). Sanitized HTML pre-populated into the editor when notes are first created. Null ⇒ a built-in
    /// default template is used. Deliberately MINIMAL: a single override field, not a template-editor subsystem.
    /// </summary>
    public string? MeetingNotesTemplate { get; set; }

    // ── Feature toggles (US-PRF-004 FR-6) ──────────────────────────────

    /// <summary>Whether 360-degree (peer) feedback is enabled for this cycle (FR-6).</summary>
    public bool Is360Enabled { get; set; }

    /// <summary>Whether a calibration phase is included (FR-6). Mirrors the presence of a Calibration phase.</summary>
    public bool IsCalibrationEnabled { get; set; }

    /// <summary>Whether peer feedback is anonymous (FR-6).</summary>
    public bool IsAnonymousFeedback { get; set; }

    // ── 360 composite-score config (US-PRF-005 FR-6) ───────────────────
    // Per-category weights for the 360 COMPOSITE score (AC-4/FR-6). They are only consulted when
    // Is360Enabled. The four weights are expected to sum to 100; the composite/final-score math normalizes
    // by the weight of the categories that actually have feedback, so a missing category never skews the
    // result. Defaults: self 10 / manager 40 / peers 30 / reports 20 (the FR-6 example).

    /// <summary>360 composite weight (%) for the Self perspective (US-PRF-005 FR-6). Default 10.</summary>
    public int ThreeSixtySelfWeightPercent { get; set; } = 10;

    /// <summary>360 composite weight (%) for the Manager perspective (US-PRF-005 FR-6). Default 40.</summary>
    public int ThreeSixtyManagerWeightPercent { get; set; } = 40;

    /// <summary>360 composite weight (%) for the Peer perspective (US-PRF-005 FR-6). Default 30.</summary>
    public int ThreeSixtyPeerWeightPercent { get; set; } = 30;

    /// <summary>360 composite weight (%) for the DirectReport perspective (US-PRF-005 FR-6). Default 20.</summary>
    public int ThreeSixtyReportWeightPercent { get; set; } = 20;

    /// <summary>
    /// Minimum number of PEER reviewers required before the 360 results may be released (US-PRF-005 BR-4/FR-3).
    /// HR is warned (not hard-blocked) when fewer peers have submitted. Default 2.
    /// </summary>
    public int Min360PeerReviewers { get; set; } = 2;

    // ── Participant scope snapshot (US-PRF-004 FR-3) ───────────────────

    /// <summary>How the participant set was resolved at creation (FR-3).</summary>
    public ParticipantScopeType ParticipantScope { get; set; } = ParticipantScopeType.AllEmployees;

    /// <summary>
    /// The department ids selected when the participant scope was <see cref="ParticipantScopeType.Departments"/>
    /// (BUG-260). Persisted ALONGSIDE the resolved <see cref="CycleParticipant"/> rows purely so the edit form can
    /// prefill the department selection — it does NOT drive participant resolution. Empty for non-Departments
    /// scopes and for cycles created before this column existed (cannot be backfilled). Mapped as a jsonb
    /// primitive collection (snake_case "scope_department_ids").
    /// </summary>
    public List<Guid> ScopeDepartmentIds { get; set; } = [];

    /// <summary>Cancellation reason (US-PRF-004 BR-6). Required when Status is Cancelled.</summary>
    public string? CancellationReason { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────
    public List<CyclePhase> Phases { get; set; } = [];
    public List<CycleParticipant> Participants { get; set; } = [];

    // ── Window gates ───────────────────────────────────────────────────
    // Prefer the loaded CyclePhase when present (the phase model is the source of truth); fall back to the
    // legacy *Start/*End columns (which the create/edit handlers keep in sync) when phases are not loaded —
    // this is what keeps US-PRF-001/002/003 services and their tests green without forcing an Include.

    /// <summary>True if the cycle is Active AND "now" is inside the goal-setting window (BR-1/AC-5).</summary>
    public bool IsGoalSettingOpen(DateTime nowUtc) => IsPhaseOpen(CyclePhaseType.GoalSetting, GoalSettingStart, GoalSettingEnd, nowUtc);

    /// <summary>True if the cycle is Active AND "now" is inside the self-assessment window (US-PRF-002 BR-1/AC-4).</summary>
    public bool IsSelfAssessmentOpen(DateTime nowUtc) => IsPhaseOpen(CyclePhaseType.SelfAssessment, SelfAssessmentStart, SelfAssessmentEnd, nowUtc);

    /// <summary>True if the cycle is Active AND "now" is inside the manager-review window (US-PRF-003 BR-1/AC-5).</summary>
    public bool IsManagerReviewOpen(DateTime nowUtc) => IsPhaseOpen(CyclePhaseType.ManagerReview, ManagerReviewStart, ManagerReviewEnd, nowUtc);

    private bool IsPhaseOpen(CyclePhaseType phaseType, DateTime legacyStart, DateTime legacyEnd, DateTime nowUtc)
    {
        if (Status != AppraisalCycleStatus.Active)
            return false;

        var phase = Phases.FirstOrDefault(p => p.PhaseType == phaseType);
        if (phase is not null)
            return phase.ContainsInstant(nowUtc);

        // Fallback: phases not loaded ⇒ consult the synced legacy columns.
        return nowUtc >= legacyStart && nowUtc <= legacyEnd;
    }

    /// <summary>The manager weight in the self-vs-manager blend (BR-4): 100 - SelfWeightPercent.</summary>
    public int ManagerWeightPercent => 100 - SelfWeightPercent;

    // ── Status state machine (US-PRF-004 FR-7) ─────────────────────────

    /// <summary>Valid status transitions (FR-7). Cancelled requires a reason (BR-6, enforced by the service).</summary>
    public static bool IsValidTransition(AppraisalCycleStatus from, AppraisalCycleStatus to) => (from, to) switch
    {
        (AppraisalCycleStatus.Draft, AppraisalCycleStatus.Active) => true,
        (AppraisalCycleStatus.Draft, AppraisalCycleStatus.Cancelled) => true,
        (AppraisalCycleStatus.Active, AppraisalCycleStatus.Paused) => true,
        (AppraisalCycleStatus.Active, AppraisalCycleStatus.Completed) => true,
        (AppraisalCycleStatus.Active, AppraisalCycleStatus.Cancelled) => true,
        (AppraisalCycleStatus.Paused, AppraisalCycleStatus.Active) => true,
        (AppraisalCycleStatus.Paused, AppraisalCycleStatus.Cancelled) => true,
        _ => false,
    };

    /// <summary>Terminal statuses no transition can leave (FR-7).</summary>
    public bool IsTerminal => Status is AppraisalCycleStatus.Completed
        or AppraisalCycleStatus.Cancelled or AppraisalCycleStatus.Closed;

    /// <summary>
    /// Syncs the legacy goal-setting / self-assessment / manager-review window columns from the current
    /// phase set, so US-PRF-001/002/003 services that read those columns observe the phase-driven windows.
    /// Called by the create/edit handlers after phases are assigned.
    /// </summary>
    public void SyncLegacyWindowsFromPhases() => SyncLegacyWindowsFromPhases(Phases);

    /// <summary>
    /// As <see cref="SyncLegacyWindowsFromPhases()"/> but reads from an EXPLICIT phase source instead of the
    /// <see cref="Phases"/> navigation. The edit path uses this so it can sync the new windows without having
    /// to mutate the tracked navigation collection (mutating it while the old phases are pending-delete makes
    /// EF's cascade fixup resurrect a deleted child as a modified row on save).
    /// </summary>
    public void SyncLegacyWindowsFromPhases(IEnumerable<CyclePhase> phases)
    {
        var goal = phases.FirstOrDefault(p => p.PhaseType == CyclePhaseType.GoalSetting);
        if (goal is not null) { GoalSettingStart = goal.StartDate; GoalSettingEnd = goal.EndDate; }

        var self = phases.FirstOrDefault(p => p.PhaseType == CyclePhaseType.SelfAssessment);
        if (self is not null) { SelfAssessmentStart = self.StartDate; SelfAssessmentEnd = self.EndDate; }

        var mgr = phases.FirstOrDefault(p => p.PhaseType == CyclePhaseType.ManagerReview);
        if (mgr is not null) { ManagerReviewStart = mgr.StartDate; ManagerReviewEnd = mgr.EndDate; }
    }
}
