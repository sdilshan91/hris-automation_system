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
    DateTime? LastModifiedAt);

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
    Guid? DelegationBackupUserId);

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
    Guid? DelegationBackupUserId);
