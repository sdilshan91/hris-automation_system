namespace HRM.Application.Features.Workflows.DTOs;

// ── Read models ──────────────────────────────────────────────────────────────

/// <summary>
/// One row in the workflow list (US-ADM-007 AC-1): name, entity type, step count, status, default flag,
/// version, last-modified. The FE groups by <see cref="EntityType"/>.
/// </summary>
public sealed record WorkflowListItemDto(
    Guid Id,
    Guid LineageId,
    string Name,
    string EntityType,
    int Version,
    string Status,
    bool IsActive,
    bool IsDefault,
    int StepCount,
    DateTime CreatedAt,
    DateTime? LastModifiedAt,
    // US-ADM-011c FR-10: the real count of live (InProgress) runtime instances on this lineage.
    int InFlightCount = 0);

// ── US-ADM-011c read models (FR-10/FR-12) ────────────────────────────────────

/// <summary>
/// US-ADM-011c FR-12: a live workflow INSTANCE with its ordered step chain (materialized skipped + pending +
/// decided rows). Tenant-scoped via the global query filter (AC-9).
/// </summary>
public sealed record WorkflowInstanceDetailDto(
    Guid InstanceId,
    string EntityType,
    Guid EntityId,
    Guid LineageId,
    int Version,
    string Status,
    int CurrentStepOrder,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<WorkflowInstanceStepDto> Steps);

/// <summary>One step-instance row in a <see cref="WorkflowInstanceDetailDto"/>'s chain.</summary>
public sealed record WorkflowInstanceStepDto(
    int StepOrder,
    bool IsParallel,
    string ApproverType,
    Guid? AssignedApproverUserId,
    string Decision,
    Guid? DecidedByUserId,
    DateTime? DecidedAt,
    string? Comments,
    DateTime? SlaDueAt,
    DateTime? EscalatedAt);

/// <summary>US-ADM-011c FR-10: one row in the admin instance list for a workflow definition lineage.</summary>
public sealed record WorkflowInstanceListItemDto(
    Guid InstanceId,
    Guid EntityId,
    int Version,
    string Status,
    int CurrentStepOrder,
    DateTime CreatedAt,
    DateTime? CompletedAt);

/// <summary>Paged admin instance list for a definition lineage (FR-10).</summary>
public sealed record PagedWorkflowInstancesDto(
    IReadOnlyList<WorkflowInstanceListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>The full workflow definition with its ordered steps (US-ADM-007 Get).</summary>
public sealed record WorkflowDetailDto(
    Guid Id,
    Guid LineageId,
    string Name,
    string EntityType,
    int Version,
    string Status,
    bool IsActive,
    bool IsDefault,
    DateTime CreatedAt,
    DateTime? LastModifiedAt,
    IReadOnlyList<WorkflowStepDto> Steps);

/// <summary>A single step in a workflow definition (US-ADM-007 FR-2).</summary>
public sealed record WorkflowStepDto(
    int StepOrder,
    string ApproverType,
    Guid? ApproverIdentifier,
    bool IsParallel,
    int SlaHours,
    string? EscalationApproverType,
    Guid? EscalationApproverIdentifier,
    string? ConditionJson,
    bool DelegationEnabled,
    Guid? DelegationBackupUserId,
    // US-ADM-011b: additional NamedUser approver ids for a parallel step (beyond the primary approver #1).
    IReadOnlyList<Guid>? ParallelApproverIdentifiers = null);

// ── Write requests ───────────────────────────────────────────────────────────

/// <summary>Create a new workflow definition (US-ADM-007 AC-2/FR-1).</summary>
public sealed record CreateWorkflowRequest(
    string Name,
    string EntityType,
    bool Activate,
    IReadOnlyList<WorkflowStepRequest> Steps);

/// <summary>
/// Update a workflow (US-ADM-007 AC-3/FR-3). Editing an ACTIVE workflow creates a NEW VERSION; the prior
/// version row is retained. <see cref="LineageId"/> identifies the logical workflow to version.
/// </summary>
public sealed record UpdateWorkflowRequest(
    string Name,
    IReadOnlyList<WorkflowStepRequest> Steps);

/// <summary>A step in a create/update request (US-ADM-007 FR-2).</summary>
public sealed record WorkflowStepRequest(
    int StepOrder,
    string ApproverType,
    Guid? ApproverIdentifier,
    bool IsParallel,
    int SlaHours,
    string? EscalationApproverType,
    Guid? EscalationApproverIdentifier,
    string? ConditionJson,
    bool DelegationEnabled,
    Guid? DelegationBackupUserId,
    // US-ADM-011b: additional NamedUser approver ids for a parallel step (the FE emits these). Null for
    // sequential steps or a primary-only parallel step.
    IReadOnlyList<Guid>? ParallelApproverIdentifiers = null);
