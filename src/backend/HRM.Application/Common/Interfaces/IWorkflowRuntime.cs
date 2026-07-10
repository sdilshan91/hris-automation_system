using HRM.Application.Common.Models;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-011 (Phase 1) approval-workflow RUNTIME engine. Owns the live instance/step-instance state machine:
/// instantiating a workflow on request submission (with a definition version snapshot), routing sequentially
/// through the configured steps using the pure <see cref="HRM.Domain.Workflows.WorkflowEvaluator"/> for
/// condition-skip, and applying approver decisions with a single-winner transactional advance.
///
/// <para>The runtime is DOMAIN-AGNOSTIC: it never mutates the underlying domain request (leave/etc.) itself.
/// Instead, on a terminal transition it invokes the caller-supplied side-effect callbacks
/// (<c>onApproved</c>/<c>onRejected</c>) INSIDE its own transaction, so the workflow state and the domain
/// side-effect (e.g. the leave ledger deduction) commit atomically. This keeps the engine reusable across
/// entity types without a DI cycle back into each domain service.</para>
/// </summary>
public interface IWorkflowRuntime
{
    /// <summary>
    /// AC-1/AC-7/AC-11: on request submission, find the tenant's Active <see cref="WorkflowDefinition"/> for
    /// <paramref name="entityType"/>. If one exists (and at least one step applies to
    /// <paramref name="requestData"/>), create a <see cref="Domain.Entities.WorkflowInstance"/> bound to that
    /// version, materialize the leading skipped steps + the first applicable step (Pending, with an SLA due
    /// time), and return the new instance id. If there is NO active definition (or no applicable steps), return
    /// a not-created result so the caller keeps its legacy single-level path (AC-11).
    /// </summary>
    Task<WorkflowInstanceCreationResult> CreateInstanceOnSubmitAsync(
        WorkflowEntityType entityType,
        Guid entityId,
        Guid? requesterEmployeeId,
        IReadOnlyDictionary<string, object?> requestData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-2/AC-3/AC-10/AC-12: apply the current user's decision to the active Pending step of the in-flight
    /// instance for (<paramref name="entityType"/>, <paramref name="entityId"/>). Only the resolved approver of
    /// the Pending step may decide (else 403, AC-10). On approve, the instance advances to the next applicable
    /// step (skipping condition-unmet steps, AC-3) or completes; on the terminal transition the matching
    /// side-effect callback runs inside the same retry-safe transaction (AC-12/NFR-4) so exactly one caller
    /// wins and the domain side-effect commits atomically with the workflow state.
    /// </summary>
    Task<Result<WorkflowDecisionResult>> DecideAsync(
        WorkflowEntityType entityType,
        Guid entityId,
        WorkflowDecisionAction action,
        string? comment,
        Func<CancellationToken, Task<Result>>? onApproved = null,
        Func<CancellationToken, Task>? onRejected = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-8/FR-10: the real count of live (<see cref="WorkflowInstanceStatus.InProgress"/>) instances on a
    /// definition lineage, tenant-scoped. Used by <c>WorkflowService.DeleteAsync</c> to block deletion of a
    /// definition with in-flight requests (409 <c>workflow_in_flight</c>).
    /// </summary>
    Task<int> CountInFlightAsync(Guid lineageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the (entity-type, entity-id) an instance governs, tenant-scoped. Used by the generic decision
    /// endpoint to route a decision to the owning domain service (Phase 1: Leave). Null when not found.
    /// </summary>
    Task<WorkflowInstanceEntityRef?> GetInstanceEntityAsync(Guid instanceId, CancellationToken cancellationToken = default);
}

/// <summary>The domain request an instance governs — resolved for the generic decision endpoint's routing.</summary>
public sealed record WorkflowInstanceEntityRef(WorkflowEntityType EntityType, Guid EntityId, WorkflowInstanceStatus Status);

/// <summary>The decision an approver takes on the active step.</summary>
public enum WorkflowDecisionAction
{
    Approve = 0,
    Reject = 1
}

/// <summary>The result of <see cref="IWorkflowRuntime.CreateInstanceOnSubmitAsync"/> (AC-1/AC-11).</summary>
/// <param name="InstanceCreated">
/// True when an Active definition matched and a <see cref="Domain.Entities.WorkflowInstance"/> was created;
/// false when the caller must use the legacy single-level path (no active definition / no applicable steps).
/// </param>
/// <param name="InstanceId">The new instance id when <paramref name="InstanceCreated"/> is true; else null.</param>
public readonly record struct WorkflowInstanceCreationResult(bool InstanceCreated, Guid? InstanceId)
{
    public static WorkflowInstanceCreationResult Legacy() => new(false, null);
    public static WorkflowInstanceCreationResult Created(Guid instanceId) => new(true, instanceId);
}

/// <summary>What happened to the instance after a decision (AC-2).</summary>
public enum WorkflowDecisionOutcome
{
    /// <summary>The instance advanced to the next applicable step; the request stays pending.</summary>
    StepAdvanced = 0,

    /// <summary>The final step approved; the instance (and request) are Approved.</summary>
    InstanceApproved = 1,

    /// <summary>A step was rejected; the instance (and request) are Rejected.</summary>
    InstanceRejected = 2
}

/// <summary>The result of a successful <see cref="IWorkflowRuntime.DecideAsync"/> call.</summary>
/// <param name="InstanceId">The affected instance.</param>
/// <param name="Outcome">The resulting transition (advanced / approved / rejected).</param>
/// <param name="NextStepOrder">
/// On <see cref="WorkflowDecisionOutcome.StepAdvanced"/>, the newly-active step order; otherwise null.
/// </param>
public sealed record WorkflowDecisionResult(Guid InstanceId, WorkflowDecisionOutcome Outcome, int? NextStepOrder);
