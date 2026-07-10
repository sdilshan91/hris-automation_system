using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A live approval-workflow INSTANCE (US-ADM-011 FR-1) — one per submitted request that matched an Active
/// <see cref="WorkflowDefinition"/> for its <see cref="WorkflowEntityType"/>. Tenant-scoped via
/// <see cref="BaseEntity"/> (RLS-eligible; global query filter in <c>AppDbContext</c>).
///
/// <para><b>Version snapshot (BR-1/AC-7):</b> the instance is permanently bound to the specific definition
/// version row it was created under — <see cref="WorkflowDefinitionId"/> plus the copied
/// <see cref="LineageId"/>/<see cref="Version"/>. A later edit that creates a new version on the same lineage
/// does NOT retroactively re-route an in-flight instance.</para>
/// </summary>
public sealed class WorkflowInstance : BaseEntity
{
    /// <summary>FK to the SPECIFIC definition version row this instance snapshotted (BR-1).</summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>Copied from the definition — the stable lineage id shared by every version (AC-7/FR-10).</summary>
    public Guid LineageId { get; set; }

    /// <summary>Copied from the definition — the version number the instance is bound to (BR-1).</summary>
    public int Version { get; set; }

    /// <summary>The request type this instance governs (matches the definition's entity type).</summary>
    public WorkflowEntityType EntityType { get; set; }

    /// <summary>The id of the underlying domain request (e.g. the leave request id) — FR-1.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Current lifecycle status (FR-1).</summary>
    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.InProgress;

    /// <summary>The <see cref="WorkflowStepInstance.StepOrder"/> of the active step (0 before first activation).</summary>
    public int CurrentStepOrder { get; set; }

    /// <summary>
    /// The evaluated route — the comma-separated ordered <see cref="WorkflowStep.StepOrder"/>s that
    /// <see cref="HRM.Domain.Workflows.WorkflowEvaluator"/> found APPLICABLE for this request, snapshotted at
    /// creation (BR-1/AC-3). Steps not in this set were condition-skipped and are materialized as
    /// <see cref="WorkflowStepDecision.Skipped"/> rows. The runtime reads this to advance to the next applicable
    /// step without re-reading the (domain-owned) request data.
    /// </summary>
    public string RouteStepOrders { get; set; } = string.Empty;

    /// <summary>
    /// The submitting user (audit/reference). Snapshotted at creation so approver resolution and history do not
    /// depend on re-reading the domain request.
    /// </summary>
    public Guid? RequesterUserId { get; set; }

    /// <summary>
    /// The submitting employee — used to resolve <c>LineManager</c>/<c>DepartmentHead</c> approver types at each
    /// step activation (FR-5). Snapshotted at creation.
    /// </summary>
    public Guid? RequesterEmployeeId { get; set; }

    /// <summary>Set when the instance reaches a terminal state (Approved/Rejected/Cancelled) — FR-1.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>The ordered step-instances (including materialized Skipped rows — §13 Q4).</summary>
    public ICollection<WorkflowStepInstance> StepInstances { get; set; } = new List<WorkflowStepInstance>();
}
