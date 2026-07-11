using HRM.Application.Common.Models;
using HRM.Application.Features.Benefits.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Benefit eligibility-rule administration + employee enrollment service (US-TRN-003). All operations are
/// tenant-scoped via <see cref="ITenantContext"/> and the EF global query filters. Rule CRUD + enroll-on-behalf
/// require <c>Benefits.Manage</c>; self-service enroll/terminate/read requires <c>Benefits.View.Own</c>; any-employee
/// reads require <c>Benefits.View.All</c>/<c>Manage</c> (finer self-vs-others gates are enforced in the service).
/// </summary>
public interface IBenefitEnrollmentService
{
    // ── Eligibility rules (Manage) ──────────────────────────────────────────────
    Task<Result<EligibilityRuleDto>> AddRuleAsync(Guid planId, CreateEligibilityRuleRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<EligibilityRuleDto>>> GetRulesAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);

    // ── Eligible plans ──────────────────────────────────────────────────────────
    Task<Result<IReadOnlyList<EligiblePlanDto>>> GetMyEligiblePlansAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<EligiblePlanDto>>> GetEligiblePlansAsync(Guid employeeId, CancellationToken cancellationToken = default);

    // ── Enrollment ──────────────────────────────────────────────────────────────
    Task<Result<BenefitEnrollmentDto>> EnrollAsync(EnrollRequest request, CancellationToken cancellationToken = default);
    Task<Result<BenefitEnrollmentDto>> TerminateAsync(Guid enrollmentId, TerminateEnrollmentRequest request, CancellationToken cancellationToken = default);

    // ── Enrollment reads ────────────────────────────────────────────────────────
    Task<Result<IReadOnlyList<BenefitEnrollmentDto>>> GetMyEnrollmentsAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<BenefitEnrollmentDto>>> GetEmployeeEnrollmentsAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
