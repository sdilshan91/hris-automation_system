using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A recorded exit interview for a departing employee (US-ONB-006). Linked to an
/// <see cref="OffboardingInstance"/> (FR-3) and owns an immutable set of <see cref="ExitInterviewResponse"/>
/// rows. Recorded either by HR (<see cref="ExitInterviewMode.HrConducted"/>) or by the employee
/// (<see cref="ExitInterviewMode.SelfService"/>, FR-2). Tenant-scoped via <see cref="BaseEntity.TenantId"/> +
/// the EF global query filter + <c>TenantInterceptor</c> (FR-6/NFR-2/AC-5).
///
/// BR-1: only ONE active (non-superseded) exit interview per offboarding instance. BR-2: a submitted exit
/// interview is immutable — an edit creates a NEW <see cref="Version"/> and marks the prior row
/// <see cref="IsSuperseded"/> (the original is preserved). Maps to the "exit_interview" table (snake_case).
/// </summary>
public sealed class ExitInterview : BaseEntity
{
    /// <summary>FK to the owning <see cref="OffboardingInstance"/> (FR-3). One active interview per instance (BR-1).</summary>
    public Guid OffboardingInstanceId { get; set; }

    /// <summary>The template this interview was recorded against (the active tenant template at record time).</summary>
    public Guid TemplateId { get; set; }

    /// <summary>How the interview was conducted (FR-2).</summary>
    public ExitInterviewMode InterviewMode { get; set; }

    /// <summary>The HR user who conducted the interview (FR-2, §7). Required when <see cref="InterviewMode"/> is HrConducted.</summary>
    public Guid? ConductedByUserId { get; set; }

    /// <summary>The date the interview was conducted (§7). Cannot be in the future.</summary>
    public DateOnly InterviewDate { get; set; }

    /// <summary>Optional overall experience rating (§7, 1-5).</summary>
    public int? OverallExperienceRating { get; set; }

    /// <summary>Optional "would recommend employer" flag (§7).</summary>
    public bool? WouldRecommendEmployer { get; set; }

    /// <summary>Optional free-text closing comments (§7, max 5000). PII — flagged on detail access (NFR-6).</summary>
    public string? AdditionalComments { get; set; }

    // ── BR-2 immutability / versioning ─────────────────────────────

    /// <summary>Version number (BR-2). The first submission is 1; each edit increments and supersedes the prior.</summary>
    public int Version { get; set; } = 1;

    /// <summary>True once a newer version supersedes this row (BR-2). Superseded rows are preserved, not deleted.</summary>
    public bool IsSuperseded { get; set; }

    /// <summary>The version this row was edited from (BR-2). Null for the first submission.</summary>
    public Guid? SupersedesId { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public OffboardingInstance? OffboardingInstance { get; set; }
    public List<ExitInterviewResponse> Responses { get; set; } = [];
}
