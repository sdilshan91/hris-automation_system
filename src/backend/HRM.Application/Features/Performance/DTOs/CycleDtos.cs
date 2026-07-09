using HRM.Domain.Enums;

namespace HRM.Application.Features.Performance.DTOs;

// ── Inputs ──────────────────────────────────────────────────────────────────

/// <summary>A phase definition supplied when creating/editing a cycle (US-PRF-004 FR-2).</summary>
public sealed record CyclePhaseInput(
    CyclePhaseType PhaseType,
    DateTime StartDate,
    DateTime EndDate);

/// <summary>The participant-scope selector resolved at cycle creation (US-PRF-004 FR-3).</summary>
public sealed record ParticipantScopeInput(
    ParticipantScopeType ScopeType,
    IReadOnlyList<Guid>? DepartmentIds = null,
    IReadOnlyList<Guid>? EmployeeIds = null);

/// <summary>Full create-cycle input (US-PRF-004 AC-1/AC-2). Phases must be sequential, non-overlapping, in-range.</summary>
public sealed record CreateCycleInput(
    string Name,
    CycleType Type,
    DateTime StartDate,
    DateTime EndDate,
    IReadOnlyList<CyclePhaseInput> Phases,
    ParticipantScopeInput Scope,
    int RatingScaleMax,
    int SelfWeightPercent,
    bool Is360Enabled,
    bool IsCalibrationEnabled,
    bool IsAnonymousFeedback);

/// <summary>Edit-cycle input (US-PRF-004 AC-5). Re-validates phase dates; reschedules jobs; notifies.</summary>
public sealed record UpdateCycleInput(
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    IReadOnlyList<CyclePhaseInput> Phases,
    bool Is360Enabled,
    bool IsCalibrationEnabled,
    bool IsAnonymousFeedback,
    int? RatingScaleMax,
    int? SelfWeightPercent);

/// <summary>Clone an existing cycle as a template with a new name + shifted dates (US-PRF-004 FR-8).</summary>
public sealed record CloneCycleInput(
    Guid SourceCycleId,
    string Name,
    DateTime StartDate,
    DateTime EndDate);

// ── Outputs ─────────────────────────────────────────────────────────────────

/// <summary>A phase in a cycle's timeline (US-PRF-004 AC-3).</summary>
public sealed record CyclePhaseDto
{
    public Guid Id { get; init; }
    public CyclePhaseType PhaseType { get; init; }
    public string PhaseTypeName { get; init; } = string.Empty;
    public int Sequence { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public bool IsCurrent { get; init; }
}

/// <summary>
/// The full participant scope of a cycle (BUG-260). Exposes the id lists behind the flat
/// <see cref="ParticipantScopeType"/> so the edit form can prefill participants. <c>DepartmentIds</c> is the
/// persisted selection (only populated for a <see cref="ParticipantScopeType.Departments"/> scope; empty
/// otherwise and for pre-existing cycles). <c>EmployeeIds</c> is the resolved <c>CycleParticipant</c> set.
/// </summary>
public sealed record CycleScopeDto(
    ParticipantScopeType ScopeType,
    IReadOnlyList<Guid> DepartmentIds,
    IReadOnlyList<Guid> EmployeeIds);

/// <summary>A cycle summary row for the list view (US-PRF-004).</summary>
public sealed record CycleSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public CycleType Type { get; init; }
    public string TypeName { get; init; } = string.Empty;
    public AppraisalCycleStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int ParticipantCount { get; init; }
}

/// <summary>The full cycle record (US-PRF-004 §7).</summary>
public sealed record CycleDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public CycleType Type { get; init; }
    public string TypeName { get; init; } = string.Empty;
    public AppraisalCycleStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int RatingScaleMax { get; init; }
    public int SelfWeightPercent { get; init; }
    public int ManagerWeightPercent { get; init; }
    public bool Is360Enabled { get; init; }
    public bool IsCalibrationEnabled { get; init; }
    public bool IsAnonymousFeedback { get; init; }
    public ParticipantScopeType ParticipantScope { get; init; }

    /// <summary>
    /// The full participant scope (BUG-260) — nested id lists behind the flat <see cref="ParticipantScope"/>
    /// (kept for back-compat). Lets the edit form prefill the department/employee selection.
    /// </summary>
    public CycleScopeDto Scope { get; init; } = new(ParticipantScopeType.AllEmployees, [], []);
    public string? CancellationReason { get; init; }
    public int ParticipantCount { get; init; }
    public IReadOnlyList<CyclePhaseDto> Phases { get; init; } = [];
}

/// <summary>
/// The canonical "active cycle with current phase" payload the FEs gate windows off (US-PRF-004 AC-3, plus
/// the US-PRF-001/002/003 contract convergence). The boolean window flags are authoritative — the FE renders
/// read-only / closed-message off these, NOT by computing from dates.
/// </summary>
public sealed record ActiveCycleDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public CycleType Type { get; init; }
    public string TypeName { get; init; } = string.Empty;
    public AppraisalCycleStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int RatingScaleMax { get; init; }
    public int SelfWeightPercent { get; init; }
    public int ManagerWeightPercent { get; init; }
    public bool Is360Enabled { get; init; }

    /// <summary>The phase containing "now", if any (timeline cursor).</summary>
    public CyclePhaseType? CurrentPhase { get; init; }
    public string? CurrentPhaseName { get; init; }

    /// <summary>Authoritative goal-setting window gate (US-PRF-001 AC-5).</summary>
    public bool GoalSettingOpen { get; init; }

    /// <summary>Authoritative self-assessment window gate (US-PRF-002 AC-4).</summary>
    public bool SelfAssessmentOpen { get; init; }

    /// <summary>Authoritative manager-review window gate (US-PRF-003 AC-5).</summary>
    public bool ManagerReviewOpen { get; init; }

    public IReadOnlyList<CyclePhaseDto> Phases { get; init; } = [];
}

/// <summary>Per-phase completion stats for the cycle dashboard (US-PRF-004 AC-3).</summary>
public sealed record PhaseCompletionDto
{
    public CyclePhaseType PhaseType { get; init; }
    public string PhaseTypeName { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public bool IsCurrent { get; init; }

    /// <summary>Total participants expected to complete this phase.</summary>
    public int TotalParticipants { get; init; }

    /// <summary>How many participants have completed this phase.</summary>
    public int CompletedCount { get; init; }

    /// <summary>Completion percentage (0-100, rounded).</summary>
    public int CompletionPercent { get; init; }

    /// <summary>Participants who have NOT completed AND the phase end date has passed (US-PRF-004 AC-3).</summary>
    public int OverdueCount { get; init; }
}

/// <summary>The cycle dashboard: timeline + per-phase completion stats + overdue counts (US-PRF-004 AC-3).</summary>
public sealed record CycleDashboardDto
{
    public Guid CycleId { get; init; }
    public string Name { get; init; } = string.Empty;
    public AppraisalCycleStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int ParticipantCount { get; init; }
    public CyclePhaseType? CurrentPhase { get; init; }
    public string? CurrentPhaseName { get; init; }
    public IReadOnlyList<PhaseCompletionDto> Phases { get; init; } = [];
}
