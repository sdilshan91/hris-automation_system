namespace HRM.Domain.Entities;

/// <summary>
/// A single answer to an <see cref="ExitInterviewQuestion"/> within an <see cref="ExitInterview"/>
/// (US-ONB-006 §7). Exactly one of <see cref="Rating"/> / <see cref="SelectedOption"/> / <see cref="FreeText"/>
/// is populated per the question's type. Immutable once its parent interview is submitted (BR-2). Tenant-scoped
/// via <see cref="BaseEntity.TenantId"/> + the EF global query filter. Maps to "exit_interview_response".
/// </summary>
public sealed class ExitInterviewResponse : BaseEntity
{
    /// <summary>FK to the owning <see cref="ExitInterview"/>.</summary>
    public Guid ExitInterviewId { get; set; }

    /// <summary>The template question this answers (FR-1). Cross-ref to <see cref="ExitInterviewQuestion"/>.</summary>
    public Guid QuestionId { get; set; }

    /// <summary>Snapshot of the question category at record time (for tenant-scoped analytics grouping, FR-4).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Rating answer (1-5) for rating-type questions (§7). Null otherwise.</summary>
    public int? Rating { get; set; }

    /// <summary>Selected option for multiple-choice / yes-no questions (§7, max 200). Null otherwise.</summary>
    public string? SelectedOption { get; set; }

    /// <summary>Free-text answer (§7, max 2000). PII — never surfaced in anonymized analytics (FR-5/NFR-6).</summary>
    public string? FreeText { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public ExitInterview? ExitInterview { get; set; }
}
