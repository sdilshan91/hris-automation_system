using HRM.Application.Common.Models;
using HRM.Application.Features.Benefits.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Benefit-plan administration service (US-TRN-002). All operations are tenant-scoped via
/// <see cref="ITenantContext"/> and the EF global query filters. Plan CRUD, the Draft/Active/Inactive/Archived
/// state machine, tenant-default-currency resolution, and audit live here; thin MediatR handlers delegate to it.
/// </summary>
public interface IBenefitPlanService
{
    /// <summary>Lists the tenant's benefit plans (AC-5).</summary>
    Task<Result<IReadOnlyList<BenefitPlanDto>>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one benefit plan (AC-5).</summary>
    Task<Result<BenefitPlanDto>> GetPlanByIdAsync(Guid planId, CancellationToken cancellationToken = default);

    /// <summary>Creates a plan in <c>Draft</c> (AC-1). Blank currency resolves to the tenant default. Requires
    /// Benefits.Manage (enforced by the controller).</summary>
    Task<Result<BenefitPlanDto>> CreatePlanAsync(CreateBenefitPlanRequest request, CancellationToken cancellationToken = default);

    /// <summary>Updates plan metadata/cost/coverage/dates/window (not status); audits before/after (AC-4).</summary>
    Task<Result<BenefitPlanDto>> UpdatePlanAsync(Guid planId, UpdateBenefitPlanRequest request, CancellationToken cancellationToken = default);

    /// <summary>Transitions plan status, validating legal transitions (AC-2/AC-3/AC-6). Illegal → 409.</summary>
    Task<Result<BenefitPlanDto>> ChangeStatusAsync(Guid planId, string targetStatus, CancellationToken cancellationToken = default);
}
