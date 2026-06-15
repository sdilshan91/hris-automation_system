namespace HRM.Application.Features.Recruitment.DTOs;

// ════════════════════════════════════════════════════════════════════════
//  US-REC-009 — Recruitment Dashboard and Analytics.
//  Pinned API contract (frontend + QA build against this exact camelCase shape).
//  Base: /api/v1/recruitment, ApiResponse<T> envelope, perm Recruitment.View.
//  Read-only aggregation over applicant / applicant_stage_history / vacancy / offer
//  (all tenant-scoped via the EF global query filters). NO new entities.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Filter inputs for the recruitment dashboard (FR-6/FR-7). The date range narrows the period
/// metrics (total applicants, hires, offer acceptance, time-to-hire trend); the optional
/// department / vacancy filters drill into a specific area. Open-vacancy / vacancy-status / funnel
/// "current state" counts reflect live state, not the period (a vacancy open now is open now).
/// </summary>
public sealed record RecruitmentDashboardFilter
{
    /// <summary>Inclusive start of the reporting period (FR-6). Defaults to 30 days before <see cref="To"/>.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Inclusive end of the reporting period (FR-6). Defaults to today (UTC).</summary>
    public DateOnly? To { get; init; }

    /// <summary>Optional department drill-down (FR-7) — limits to vacancies in this department.</summary>
    public Guid? DepartmentId { get; init; }

    /// <summary>Optional vacancy drill-down (FR-7) — limits all metrics to a single vacancy.</summary>
    public Guid? VacancyId { get; init; }
}

/// <summary>
/// KPI card values for the recruitment dashboard (AC-1/FR-1).
/// averageTimeToHireDays = mean calendar days from applied_at to entering the Hired stage for
/// applicants hired within the period (BR-1). offerAcceptanceRate = accepted / sent offers * 100
/// for offers sent in the period (BR-2).
/// </summary>
public sealed record RecruitmentKpiDto
{
    /// <summary>Vacancies currently in the Open status (live state, not period-bound).</summary>
    public int OpenVacancies { get; init; }

    /// <summary>Applicants who applied within the period.</summary>
    public int TotalApplicants { get; init; }

    /// <summary>Applicants who entered the Hired stage within the period (BR-1).</summary>
    public int Hires { get; init; }

    /// <summary>Mean calendar days from applied_at to Hired for period hires (BR-1); 0 when none.</summary>
    public decimal AverageTimeToHireDays { get; init; }

    /// <summary>accepted / sent offers * 100 for offers sent in the period (BR-2, 2dp); 0 when none sent.</summary>
    public decimal OfferAcceptanceRate { get; init; }

    /// <summary>Offers currently in the Sent status awaiting a response (live state).</summary>
    public int OffersPending { get; init; }
}

/// <summary>One stage row in the recruitment funnel (AC-3/FR-2/BR-3).</summary>
public sealed record FunnelStageDto
{
    /// <summary>The pipeline stage name (PascalCase enum member, e.g. "Applied").</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>Number of applicants that reached this stage within the period.</summary>
    public int Count { get; init; }

    /// <summary>
    /// Conversion % from the previous stage to this one (count_n / count_n-1 * 100, 2dp) — BR-3.
    /// Null for the first stage (Applied), which has no predecessor.
    /// </summary>
    public decimal? ConversionRate { get; init; }
}

/// <summary>One source row in the source-effectiveness breakdown (AC-4/FR-3/BR-6).</summary>
public sealed record SourceEffectivenessDto
{
    /// <summary>The application source name (PascalCase enum member, e.g. "Referral").</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Applicants attributed to this source within the period.</summary>
    public int ApplicantCount { get; init; }

    /// <summary>Of those, the count that reached the Hired stage within the period.</summary>
    public int HireCount { get; init; }

    /// <summary>hireCount / applicantCount * 100 (2dp); 0 when no applicants from this source.</summary>
    public decimal ConversionRate { get; init; }
}

/// <summary>One point in the time-to-hire trend series (FR-4). period is a "yyyy-MM" or "yyyy-Www" bucket label.</summary>
public sealed record TimeToHireTrendPointDto
{
    /// <summary>Bucket label: "yyyy-MM" for the month bucket, "yyyy-Www" for the ISO-week bucket.</summary>
    public string Period { get; init; } = string.Empty;

    /// <summary>Mean time-to-hire (calendar days, 2dp) for applicants hired in this bucket.</summary>
    public decimal AverageDays { get; init; }

    /// <summary>Number of hires that fell in this bucket (drives the trend's data density).</summary>
    public int HireCount { get; init; }
}

/// <summary>One row in the vacancy-status summary (FR-5).</summary>
public sealed record VacancyStatusCountDto
{
    /// <summary>The vacancy status name (PascalCase enum member, e.g. "Open").</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Number of vacancies currently in this status (live state).</summary>
    public int Count { get; init; }
}

/// <summary>One event in the recent-activity feed (FR-9).</summary>
public sealed record RecruitmentActivityDto
{
    /// <summary>Machine-readable event type: "Applied" | "StageChanged" | "OfferSent" | "Hired".</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Human-readable one-line description of the event.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>UTC instant the event occurred (ISO). The feed is ordered newest-first.</summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>The applicant the event relates to (for FE deep-linking). Null if not applicable.</summary>
    public Guid? ApplicantId { get; init; }

    /// <summary>Applicant display name when known.</summary>
    public string? ApplicantName { get; init; }

    /// <summary>The vacancy the event relates to (for FE deep-linking). Null if not applicable.</summary>
    public Guid? VacancyId { get; init; }

    /// <summary>Vacancy title when known.</summary>
    public string? VacancyTitle { get; init; }
}

/// <summary>The full recruitment dashboard payload (AC-1..AC-4, FR-1..FR-5/FR-9).</summary>
public sealed record RecruitmentDashboardDto
{
    /// <summary>Inclusive period start echoed back, "yyyy-MM-dd".</summary>
    public string From { get; init; } = string.Empty;

    /// <summary>Inclusive period end echoed back, "yyyy-MM-dd".</summary>
    public string To { get; init; } = string.Empty;

    public RecruitmentKpiDto Kpis { get; init; } = new();
    public IReadOnlyList<FunnelStageDto> Funnel { get; init; } = [];
    public IReadOnlyList<SourceEffectivenessDto> Sources { get; init; } = [];
    public IReadOnlyList<TimeToHireTrendPointDto> TimeToHireTrend { get; init; } = [];
    public IReadOnlyList<VacancyStatusCountDto> VacancyStatusSummary { get; init; } = [];
    public IReadOnlyList<RecruitmentActivityDto> RecentActivity { get; init; } = [];
}

/// <summary>Result of a recruitment-dashboard export (FR-8): the file bytes + name + content type.</summary>
public sealed record RecruitmentDashboardExportResult
{
    public byte[] FileContent { get; init; } = [];
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
}
