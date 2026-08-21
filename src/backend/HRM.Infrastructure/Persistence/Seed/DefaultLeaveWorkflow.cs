using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Infrastructure.Persistence.Seed;

/// <summary>
/// GAP-029 / queue item C1 — the one definition of the default leave-approval workflow.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a shared builder and not two copies.</b> The default <i>shift</i> is seeded twice — once in
/// <c>TenantProvisioningService.SeedDefaultShift</c> and once in <c>DbInitializer.EnsureDefaultShiftAsync</c> —
/// with a comment in one saying it "mirrors" the other and nothing checking that it still does. That is this
/// codebase's systemic S-1 finding (two hand-written descriptions of one truth, with no guard) in miniature.
/// This seed refuses to repeat it: provisioning and the backfill both call <see cref="Build"/>, so a new tenant
/// and a backfilled tenant cannot drift apart.
/// </para>
/// <para>
/// <b>Why it must behave exactly like the legacy path.</b> Until now every tenant fell through
/// <c>WorkflowRuntimeService</c>'s AC-11 branch to the legacy single-level approval, because no tenant had a
/// definition. Seeding one flips real approval routing, so the seeded route is deliberately the narrowest
/// thing that reproduces legacy behaviour:
/// </para>
/// <list type="bullet">
///   <item><description>ONE step — legacy is single-level.</description></item>
///   <item><description><see cref="WorkflowApproverType.LineManager"/> — legacy notifies and routes to
///     <c>employee.ReportsToEmployeeId</c>.</description></item>
///   <item><description><c>SlaHours = 0</c>, which <c>WorkflowRuntimeService</c> reads as "no SLA due date"
///     (<c>SlaDueAt = step.SlaHours &gt; 0 ? … : null</c>), so nothing escalates. Legacy never escalated;
///     a non-zero default would silently start escalating every tenant's leave requests.</description></item>
///   <item><description>No escalation approver, no delegation, no condition — every one of those is a
///     behaviour legacy did not have.</description></item>
/// </list>
/// <para>
/// The richer US-ADM-011 capabilities (parallel steps, SLA escalation, delegation) stay available to any admin
/// who edits this definition. The seed's job is to make the engine the live path WITHOUT changing outcomes,
/// not to enable features nobody asked for.
/// </para>
/// </remarks>
public static class DefaultLeaveWorkflow
{
    /// <summary>Name of the seeded definition. Public so tests and the backfill agree on one string.</summary>
    public const string DefinitionName = "Default Leave Approval";

    /// <summary>
    /// Builds an ACTIVE, single-step line-manager leave-approval definition for <paramref name="tenantId"/>.
    /// Not persisted — the caller adds it, so provisioning can enlist it in its own transaction.
    /// </summary>
    public static WorkflowDefinition Build(Guid tenantId, DateTime now, string actor)
    {
        var definitionId = BaseEntity.NewUuidV7();

        return new WorkflowDefinition
        {
            Id = definitionId,
            TenantId = tenantId,
            Name = DefinitionName,
            EntityType = WorkflowEntityType.Leave,
            LineageId = definitionId, // v1: lineage == own id, matching WorkflowService.CreateAsync.
            Version = 1,
            // Active, not Draft: a Draft definition is invisible to WorkflowRuntimeService (it matches on
            // Status == Active), so seeding a Draft would leave the engine exactly as dormant as before.
            Status = WorkflowStatus.Active,
            IsActive = true,
            // IsDefault marks this as the system-seeded one, which is what lets the backfill and tests
            // recognise it without string-matching the name.
            IsDefault = true,
            CreatedAt = now,
            CreatedBy = actor,
            Steps =
            [
                new WorkflowStep
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = tenantId,
                    WorkflowDefinitionId = definitionId,
                    StepOrder = 1,
                    ApproverType = WorkflowApproverType.LineManager,
                    ApproverIdentifier = null,
                    IsParallel = false,
                    SlaHours = 0,
                    EscalationApproverType = null,
                    EscalationApproverIdentifier = null,
                    ConditionJson = null,
                    DelegationEnabled = false,
                    DelegationBackupUserId = null,
                    CreatedAt = now,
                    CreatedBy = actor,
                },
            ],
        };
    }
}
