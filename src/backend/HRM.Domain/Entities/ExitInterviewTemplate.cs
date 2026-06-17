namespace HRM.Domain.Entities;

/// <summary>
/// A tenant's configurable exit-interview questionnaire template (US-ONB-006 FR-1, BR-6). Holds an ordered
/// set of <see cref="ExitInterviewQuestion"/> children grouped by free-text category (e.g. "Reason for
/// Leaving", "Work Environment", "Management", "Recommendations" — AC-1). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c> (NFR-2/AC-5).
/// When a tenant has no template, the service materializes a built-in DEFAULT set. Maps to the
/// "exit_interview_template" table (snake_case).
/// </summary>
public sealed class ExitInterviewTemplate : BaseEntity
{
    /// <summary>Template name (max 200). Defaults to the seeded "Default Exit Interview" for the auto-created set.</summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Optional free-text description (max 2000).</summary>
    public string? Description { get; set; }

    /// <summary>Active flag (BR-6). Only one active template per tenant is presented for new interviews.</summary>
    public bool IsActive { get; set; } = true;

    // ── Navigation ─────────────────────────────────────────────────
    public List<ExitInterviewQuestion> Questions { get; set; } = [];
}
