using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// An onboarding checklist assigned to a specific new hire (US-ONB-002). Created from a
/// <see cref="OnboardingChecklistTemplate"/> at assignment time and owns a set of concrete
/// <see cref="OnboardingTaskInstance"/> rows with calculated due dates. Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>.
///
/// BR-2: an employee has at most one <see cref="OnboardingChecklistStatus.Active"/> instance; replacing
/// supersedes the prior active one and bumps <see cref="Version"/>. Maps to the
/// "onboarding_checklist_instance" table.
/// </summary>
public sealed class OnboardingChecklistInstance : BaseEntity
{
    /// <summary>The new hire this checklist is assigned to (FR-2). Cross-module ref to Employee.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>The source template this instance was created from (FR-2). Cross-module ref.</summary>
    public Guid TemplateId { get; set; }

    /// <summary>Snapshot of the template name at assignment time (so history survives template edits).</summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Lifecycle status (BR-2). New instances start <see cref="OnboardingChecklistStatus.Active"/>.</summary>
    public OnboardingChecklistStatus Status { get; set; } = OnboardingChecklistStatus.Active;

    /// <summary>
    /// Effective start date the due dates are anchored to. Defaults to the employee's date_of_joining,
    /// overridable per request; if that date is in the past, due dates anchor to today instead (BR-4).
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>Assignment version (BR-2). First active = 1; each replace creates a new instance at +1.</summary>
    public int Version { get; set; } = 1;

    /// <summary>The HR user who assigned the checklist (FR-3 "HR" resolution / FR-8 audit).</summary>
    public Guid? AssignedByUserId { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public List<OnboardingTaskInstance> Tasks { get; set; } = [];
}
