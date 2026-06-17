using HRM.Application.Common.Models;
using HRM.Application.Features.Workflows.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-007: Tenant-Admin approval-workflow DEFINITION management. Every method operates ONLY on the CURRENT
/// tenant via <see cref="ITenantContext"/> — there is no tenant_id parameter, and reads/writes go through the
/// EF global query filter, so the operations are inherently isolated (BR-7). Each mutation audits before/after
/// (NFR-4).
///
/// <para><b>Scope:</b> this is the DEFINITION/configuration layer plus the pure
/// <see cref="HRM.Domain.Workflows.WorkflowEvaluator"/>. The RUNTIME engine integration (live request routing
/// through these definitions, per-request WorkflowInstance/StepInstance, SLA Hangfire timers + escalation,
/// live delegation, Redis cache) is DEFERRED and not implemented here.</para>
/// </summary>
public interface IWorkflowService
{
    /// <summary>List the current tenant's workflows, ordered by entity type then name (AC-1).</summary>
    Task<Result<IReadOnlyList<WorkflowListItemDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Get one workflow definition (a specific version row) with its ordered steps.</summary>
    Task<Result<WorkflowDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new workflow definition (AC-2/FR-1/FR-5). Validates ≥1 step, approver references, positive SLAs,
    /// well-formed conditions, and the plan limit (FR-4/AC-4). When activated, auto-archives the prior active
    /// workflow for the same entity type (BR-2). Version starts at 1.
    /// </summary>
    Task<Result<WorkflowDetailDto>> CreateAsync(
        CreateWorkflowRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a workflow (AC-3/FR-3). Editing an ACTIVE workflow creates a NEW VERSION (Version+1) and retains
    /// the prior version row; a Draft is updated in place. <paramref name="lineageId"/> identifies the logical
    /// workflow.
    /// </summary>
    Task<Result<WorkflowDetailDto>> UpdateAsync(
        Guid lineageId, UpdateWorkflowRequest request, CancellationToken cancellationToken = default);

    /// <summary>Archive a workflow (FR-6): IsActive=false, Status=Archived. Archived don't count toward the plan limit.</summary>
    Task<Result<WorkflowDetailDto>> ArchiveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Restore an archived workflow (FR-6): reactivates it, auto-archiving any current active for its type (BR-2).</summary>
    Task<Result<WorkflowDetailDto>> RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a workflow (BR-6): allowed only when there are no in-flight instances. The instance table is
    /// deferred (none exist yet), so the guard currently always passes; it is structured to be correct once
    /// instances exist.
    /// </summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
