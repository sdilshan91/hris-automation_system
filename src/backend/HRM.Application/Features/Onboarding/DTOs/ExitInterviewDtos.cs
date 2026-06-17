namespace HRM.Application.Features.Onboarding.DTOs;

// ── GET /template ───────────────────────────────────────────────────────

/// <summary>US-ONB-006 FR-1/AC-1: the tenant's exit-interview questionnaire template, grouped by category.</summary>
public sealed record ExitInterviewTemplateDto
{
    public Guid TemplateId { get; init; }
    public IReadOnlyList<ExitInterviewCategoryDto> Categories { get; init; } = [];
}

/// <summary>A category section of the questionnaire (AC-1), holding its ordered questions.</summary>
public sealed record ExitInterviewCategoryDto
{
    public string Category { get; init; } = string.Empty;
    public IReadOnlyList<ExitInterviewQuestionDto> Questions { get; init; } = [];
}

/// <summary>A single configurable question (FR-1). <c>type</c> is the snake_case wire value.</summary>
public sealed record ExitInterviewQuestionDto
{
    public Guid QuestionId { get; init; }
    public string Text { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty; // "rating" | "multiple_choice" | "free_text" | "yes_no"
    public IReadOnlyList<string>? Options { get; init; }
    public bool Required { get; init; }
}

// ── GET ?offboardingId / POST result ────────────────────────────────────

/// <summary>US-ONB-006: a recorded exit interview with its responses (FR-3).</summary>
public sealed record ExitInterviewDto
{
    public Guid Id { get; init; }
    public Guid OffboardingInstanceId { get; init; }
    public Guid TemplateId { get; init; }
    public string InterviewMode { get; init; } = string.Empty; // "hr_conducted" | "self_service"
    public Guid? ConductedByUserId { get; init; }
    public DateTime InterviewDate { get; init; }
    public int? OverallExperienceRating { get; init; }
    public bool? WouldRecommendEmployer { get; init; }
    public string? AdditionalComments { get; init; }
    public int Version { get; init; }
    public IReadOnlyList<ExitInterviewResponseDto> Responses { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}

/// <summary>A single answer (§7). Free-text is only included for detail-permitted callers (FR-5/NFR-6).</summary>
public sealed record ExitInterviewResponseDto
{
    public Guid QuestionId { get; init; }
    public string Category { get; init; } = string.Empty;
    public int? Rating { get; init; }
    public string? SelectedOption { get; init; }
    public string? FreeText { get; init; }
}

// ── POST input ──────────────────────────────────────────────────────────

/// <summary>US-ONB-006 POST body (§7). interview_mode is "hr_conducted" | "self_service".</summary>
public sealed record RecordExitInterviewInput(
    Guid OffboardingId,
    string InterviewMode,
    DateTime InterviewDate,
    IReadOnlyList<ExitInterviewResponseInput> Responses,
    int? OverallExperienceRating,
    bool? WouldRecommendEmployer,
    string? AdditionalComments);

/// <summary>A single response in the POST body (§7).</summary>
public sealed record ExitInterviewResponseInput(
    Guid QuestionId,
    int? Rating,
    string? SelectedOption,
    string? FreeText);

// ── GET /analytics (FR-4/FR-5 anonymized aggregates) ─────────────────────

/// <summary>US-ONB-006 FR-4: tenant-scoped aggregated analytics (AGGREGATES ONLY — anonymized, FR-5).</summary>
public sealed record ExitInterviewAnalyticsDto
{
    public IReadOnlyList<ReasonCountDto> ReasonDistribution { get; init; } = [];
    public IReadOnlyList<CategoryAverageDto> AverageRatingsPerCategory { get; init; } = [];
    public IReadOnlyList<TrendPointDto> Trend { get; init; } = [];
}

/// <summary>Reason-for-leaving distribution bucket (pie chart, AC-4).</summary>
public sealed record ReasonCountDto
{
    public string Reason { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>Average rating per questionnaire category (bar chart, AC-4).</summary>
public sealed record CategoryAverageDto
{
    public string Category { get; init; } = string.Empty;
    public double AverageRating { get; init; }
}

/// <summary>A monthly trend point — interview count + average overall rating (line chart, AC-4).</summary>
public sealed record TrendPointDto
{
    public string Period { get; init; } = string.Empty; // "yyyy-MM"
    public int Count { get; init; }
    public double AverageRating { get; init; }
}
