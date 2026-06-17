namespace HRM.Domain.Entities;

/// <summary>
/// A reusable onboarding checklist template within a tenant (US-ONB-001). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> and the EF global query filter + <c>TenantInterceptor</c>
/// (this codebase enforces tenant isolation with EF query filters, not Postgres RLS — same as every
/// other module). Holds an ordered set of <see cref="OnboardingTemplateTask"/> children grouped by
/// free-text category. Maps to the "onboarding_checklist_template" table.
/// </summary>
public sealed class OnboardingChecklistTemplate : BaseEntity
{
    /// <summary>Template name (AC-1, required, min 3 / max 200). Unique per tenant (BR-1).</summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Optional free-text description (max 2000).</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Department ids this template applies to (FR-4). Empty = universal (applies to all departments).
    /// Stored as a Postgres uuid[] column. Cross-module reference (no hard FK) validated in the service.
    /// </summary>
    public List<Guid> ApplicableDepartments { get; set; } = [];

    /// <summary>
    /// Job-title ids this template applies to (FR-4). Empty = universal (applies to all job titles).
    /// Stored as a Postgres uuid[] column. Cross-module reference (no hard FK) validated in the service.
    /// </summary>
    public List<Guid> ApplicableJobTitles { get; set; } = [];

    /// <summary>
    /// Active flag (FR-7, BR-4). Deactivated templates remain visible for historical reference but cannot
    /// be assigned to new hires. Defaults to true on create.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // ── Navigation ─────────────────────────────────────────────────
    public List<OnboardingTemplateTask> Tasks { get; set; } = [];
}
