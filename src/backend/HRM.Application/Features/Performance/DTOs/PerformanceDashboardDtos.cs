namespace HRM.Application.Features.Performance.DTOs;

// ════════════════════════════════════════════════════════════════════════
//  US-PRF-007 — Performance Dashboard and Analytics.
//  Pinned API contract (frontend + QA build against this exact camelCase shape).
//  Base: /api/v1/tenant/performance, ApiResponse<T> envelope.
//  Read-only aggregation over manager_review / self_assessment / goal / feedback_360 /
//  appraisal_cycle / cycle_participant + Core HR (employee / department / job_title / location),
//  all tenant-scoped via the EF global query filters (NFR-2). NO new persisted entities.
//
//  Scope (BR-1/BR-3/AC-5): HR (Performance.View.All) gets org-wide data + top/bottom performers;
//  a manager (Performance.View.Team) gets ONLY their direct reports + a team ranking (no org-wide
//  top/bottom). The DashboardScope echoes which scope the server applied.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Filter inputs for the performance dashboard (FR-4). All filters COMBINE (logical AND). The cycle
/// filter is the primary axis (most metrics are per-cycle); when omitted the service uses the most
/// recently started cycle in the tenant. Department / grade / employment-type / location narrow the
/// employee population. <see cref="TopBottomCount"/> sizes the top/bottom performer lists (FR-3,
/// default 10). <see cref="IncludeProbation"/> opts probation-cycle employees back in (BR-2).
/// </summary>
public sealed record PerformanceDashboardFilter
{
    /// <summary>Cycle to report on (FR-4). Null ⇒ the tenant's most recently started cycle.</summary>
    public Guid? CycleId { get; init; }

    /// <summary>Optional department drill-down (AC-2/FR-4) — limits to employees in this department.</summary>
    public Guid? DepartmentId { get; init; }

    /// <summary>Optional grade/band filter (FR-4) — limits to employees whose job title carries this grade id.</summary>
    public Guid? GradeId { get; init; }

    /// <summary>Optional employment-type filter (FR-4) — PascalCase enum member, e.g. "FullTime".</summary>
    public string? EmploymentType { get; init; }

    /// <summary>Optional location filter (FR-4) — limits to employees assigned this location id.</summary>
    public Guid? LocationId { get; init; }

    /// <summary>Size of the top-N / bottom-N performer lists (FR-3, default 10, clamped 1..100).</summary>
    public int TopBottomCount { get; init; } = 10;

    /// <summary>Include employees on a Probation cycle in the distribution (BR-2). Default false.</summary>
    public bool IncludeProbation { get; init; }
}

/// <summary>The scope the server applied to the dashboard (AC-5/BR-1/BR-3).</summary>
public enum PerformanceDashboardScope
{
    /// <summary>HR / org-wide view (Performance.View.All) — all employees, top/bottom performer lists.</summary>
    Organization = 0,

    /// <summary>Manager view (Performance.View.Team) — only the caller's direct reports, team ranking.</summary>
    Team = 1,
}

/// <summary>One histogram bucket in the score-distribution chart (AC-1/FR-1).</summary>
public sealed record ScoreDistributionBucketDto
{
    /// <summary>Inclusive lower bound of the bucket on the rating scale (e.g. 1, 2, 3, 4).</summary>
    public decimal RangeStart { get; init; }

    /// <summary>Exclusive upper bound of the bucket; the top bucket is inclusive of the scale max.</summary>
    public decimal RangeEnd { get; init; }

    /// <summary>Human label for the bucket, e.g. "3.0–4.0".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Number of scored employees whose final score falls in this bucket.</summary>
    public int Count { get; init; }
}

/// <summary>Department-wise average performance bar (AC-1/AC-2/FR-2).</summary>
public sealed record DepartmentAverageDto
{
    /// <summary>The department id (for the FR-5 drill-down).</summary>
    public Guid DepartmentId { get; init; }

    /// <summary>The department name.</summary>
    public string DepartmentName { get; init; } = string.Empty;

    /// <summary>Number of employees in this department with a final score.</summary>
    public int Headcount { get; init; }

    /// <summary>Mean final score across that department's scored employees (2dp); 0 when none.</summary>
    public decimal AverageScore { get; init; }
}

/// <summary>One performer row in the top-N / bottom-N list or the team ranking (FR-3/BR-3).</summary>
public sealed record PerformerDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;
    public Guid DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;

    /// <summary>The employee's final combined score for the cycle (manager_review.final_score, BR-4).</summary>
    public decimal Score { get; init; }
}

/// <summary>Cycle progress / completion metrics (AC-1/FR-6).</summary>
public sealed record CycleProgressDto
{
    /// <summary>Total participants enrolled in the cycle (cycle_participant rows, or the in-scope population).</summary>
    public int TotalParticipants { get; init; }

    /// <summary>Participants with at least one goal set (goal-setting complete, FR-6).</summary>
    public int GoalSettingCompleted { get; init; }

    /// <summary>Participants whose self-assessment is Submitted (FR-6).</summary>
    public int SelfAssessmentCompleted { get; init; }

    /// <summary>Participants whose manager review is Submitted (FR-6).</summary>
    public int ManagerReviewCompleted { get; init; }

    /// <summary>Participants whose review has been signed off (FR-6).</summary>
    public int SignedOff { get; init; }

    /// <summary>
    /// ISSUE-350: participants with at least one calibration applied (US-PRF-011). Before this the progress
    /// panel tracked GoalSetting → SelfAssessment → ManagerReview → SignedOff and knew nothing about
    /// calibration, so a cycle sitting IN the Calibration phase showed no movement at all — the phase looked
    /// permanently 0% and an HR lead running a calibration session had no way to see how far it had got.
    ///
    /// <para><c>null</c> when the cycle has calibration DISABLED, which is meaningfully different from
    /// <c>0</c>: null means "not part of this cycle", zero means "enabled but nobody calibrated yet". Rendering
    /// 0% for a cycle that never uses calibration is the original complaint restated.</para>
    /// </summary>
    public int? CalibrationCompleted { get; init; }

    /// <summary>
    /// Overall completion rate (AC-1): managerReviewCompleted / totalParticipants * 100 (2dp); 0 when no
    /// participants. This is the headline "% of reviews completed" KPI.
    /// </summary>
    public decimal CompletionRate { get; init; }
}

/// <summary>The full performance dashboard overview payload (AC-1/AC-2/FR-1..FR-3/FR-6).</summary>
public sealed record PerformanceDashboardDto
{
    /// <summary>The cycle this dashboard reports on.</summary>
    public Guid CycleId { get; init; }

    /// <summary>The cycle name.</summary>
    public string CycleName { get; init; } = string.Empty;

    /// <summary>Which scope the server applied (Organization vs Team) — AC-5.</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>The rating-scale max the cycle uses (drives the histogram bucket count / x-axis).</summary>
    public int RatingScaleMax { get; init; }

    /// <summary>Count of employees in scope who have a final score (the distribution denominator).</summary>
    public int ScoredEmployeeCount { get; init; }

    /// <summary>Mean final score across all scored employees in scope (2dp); 0 when none (AC-1).</summary>
    public decimal AverageScore { get; init; }

    public CycleProgressDto Progress { get; init; } = new();

    public IReadOnlyList<ScoreDistributionBucketDto> ScoreDistribution { get; init; } = [];

    public IReadOnlyList<DepartmentAverageDto> DepartmentAverages { get; init; } = [];

    /// <summary>Top-N performers (Organization scope) OR the team ranking (Team scope), desc by score (FR-3/BR-3).</summary>
    public IReadOnlyList<PerformerDto> TopPerformers { get; init; } = [];

    /// <summary>Bottom-N performers — Organization scope ONLY; empty for a manager (BR-3).</summary>
    public IReadOnlyList<PerformerDto> BottomPerformers { get; init; } = [];
}

/// <summary>One employee row in a department drill-down (FR-5/AC-2).</summary>
public sealed record DepartmentEmployeeScoreDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;
    public string JobTitle { get; init; } = string.Empty;

    /// <summary>The employee's final score for the cycle, or null if not yet scored.</summary>
    public decimal? Score { get; init; }

    /// <summary>The review status name (Completed / ManagerReviewPending / PendingSelfAssessment / NotStarted).</summary>
    public string Status { get; init; } = string.Empty;
}

/// <summary>The department drill-down payload (FR-5): the department + its employee score list.</summary>
public sealed record DepartmentDrilldownDto
{
    public Guid CycleId { get; init; }
    public Guid DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public int Headcount { get; init; }
    public decimal AverageScore { get; init; }
    public IReadOnlyList<DepartmentEmployeeScoreDto> Employees { get; init; } = [];
}

/// <summary>One cycle point in a multi-cycle trend series (AC-3/FR-7).</summary>
public sealed record CycleTrendPointDto
{
    public Guid CycleId { get; init; }
    public string CycleName { get; init; } = string.Empty;

    /// <summary>Cycle start (UTC) — drives the line-chart x-ordering.</summary>
    public DateTime StartDate { get; init; }

    /// <summary>Mean final score across scored employees in this cycle, in scope (2dp); 0 when none.</summary>
    public decimal AverageScore { get; init; }

    /// <summary>Number of scored employees in this cycle.</summary>
    public int ScoredEmployeeCount { get; init; }
}

/// <summary>One department's series in the optional per-department trend overlay (AC-3/FR-7).</summary>
public sealed record DepartmentTrendSeriesDto
{
    public Guid DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;

    /// <summary>The department's average score per cycle, aligned to the overall <see cref="PerformanceTrendDto.Points"/>.</summary>
    public IReadOnlyList<CycleTrendPointDto> Points { get; init; } = [];
}

/// <summary>The multi-cycle trend payload (AC-3/FR-7).</summary>
public sealed record PerformanceTrendDto
{
    /// <summary>Which scope the server applied (Organization vs Team) — AC-5.</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>The overall average-score series across the selected cycles, ordered by cycle start.</summary>
    public IReadOnlyList<CycleTrendPointDto> Points { get; init; } = [];

    /// <summary>Optional per-department overlay series (empty unless overlay requested).</summary>
    public IReadOnlyList<DepartmentTrendSeriesDto> DepartmentSeries { get; init; } = [];
}

/// <summary>Result of a dashboard export (FR-8): the file bytes + name + content type.</summary>
public sealed record PerformanceDashboardExportResult
{
    public byte[] FileContent { get; init; } = [];
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
}
