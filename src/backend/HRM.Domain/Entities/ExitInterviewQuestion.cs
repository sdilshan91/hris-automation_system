using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A single configurable question within an <see cref="ExitInterviewTemplate"/> (US-ONB-006 FR-1). Grouped by
/// a free-text category (AC-1), with an ordered position, a question type (FR-1), an optional fixed option
/// list (multiple-choice), and a required flag. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF
/// global query filter. Maps to the "exit_interview_question" table (snake_case).
/// </summary>
public sealed class ExitInterviewQuestion : BaseEntity
{
    /// <summary>FK to the owning <see cref="ExitInterviewTemplate"/>.</summary>
    public Guid TemplateId { get; set; }

    /// <summary>Question category / section header (AC-1, max 100). E.g. "Reason for Leaving".</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The question text presented to the respondent (FR-1, max 500).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The answer shape (FR-1): rating / multiple_choice / free_text / yes_no.</summary>
    public ExitInterviewQuestionType Type { get; set; }

    /// <summary>
    /// Fixed option list for multiple-choice questions (FR-1). Stored as a Postgres text[] column.
    /// Empty for non-multiple-choice types.
    /// </summary>
    public List<string> Options { get; set; } = [];

    /// <summary>Whether an answer is required (FR-1, §7 per-type required).</summary>
    public bool Required { get; set; }

    /// <summary>Sort order within the template/category (FR-1, &gt;= 0).</summary>
    public int SortOrder { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public ExitInterviewTemplate? Template { get; set; }
}
