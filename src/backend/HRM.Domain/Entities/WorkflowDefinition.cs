using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A tenant-scoped approval-workflow definition (US-ADM-007 FR-1). Governs how requests of one
/// <see cref="WorkflowEntityType"/> are routed for approval (sequential/parallel steps, SLAs, conditions,
/// escalation, delegation).
///
/// <para><b>Versioning (FR-3/AC-3):</b> editing an ACTIVE definition does NOT mutate it in place — a NEW row
/// with <see cref="Version"/> + 1 is created and the prior version is retained (so in-flight instances could
/// reference it once the runtime engine lands). All versions of a logical workflow share the same
/// <see cref="LineageId"/>; <see cref="Version"/> is the version number within that lineage.</para>
///
/// <para><b>One-active-per-type (BR-2):</b> at most one definition per (tenant, entity type) is
/// <see cref="WorkflowStatus.Active"/> at a time; creating/restoring an active one auto-archives the prior.</para>
///
/// <para>Steps are a CHILD TABLE (<see cref="WorkflowStep"/>) rather than an owned jsonb collection — they are
/// individually queryable, conventional for the codebase, and each step carries its own tenant-scoped row.</para>
/// </summary>
public sealed class WorkflowDefinition : BaseEntity, IAuditExempt
{
    /// <summary>Human-readable workflow name (FR-1). Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The request type this workflow governs (FR-1). One active per type per tenant (BR-2).</summary>
    public WorkflowEntityType EntityType { get; set; }

    /// <summary>
    /// Stable identifier shared by every version of one logical workflow (FR-3). Set to the definition's own
    /// <see cref="BaseEntity.Id"/> for v1; carried forward unchanged on each new version.
    /// </summary>
    public Guid LineageId { get; set; }

    /// <summary>Version number within the <see cref="LineageId"/> lineage (FR-1/FR-3). Starts at 1.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Lifecycle status (AC-1/FR-6): Draft / Active / Archived.</summary>
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Draft;

    /// <summary>True when this is the live workflow for its entity type (mirrors <see cref="WorkflowStatus.Active"/>).</summary>
    public bool IsActive { get; set; }

    /// <summary>True when seeded as a tenant default during provisioning (AC-1). Defaults can still be edited.</summary>
    public bool IsDefault { get; set; }

    /// <summary>The ordered approval steps (child table). Always at least one for a valid definition (FR-5).</summary>
    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
}
