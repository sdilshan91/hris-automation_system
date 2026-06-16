using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Downstream-integration SEAM for approved recommendations (US-PRF-010 BR-6). When a recommendation reaches final
/// approval, the recommendation service RAISES one of these so the downstream modules can act:
///   - Promotion / Increment → Core HR (update grade/band/compensation effective the recommendation's date).
///   - Bonus / Increment      → Payroll (one-time earning / recurring increment).
///   - TrainingNomination     → Training (create an enrollment record).
///
/// IMPORTANT — NOT WIRED IN THIS STORY. Real cross-module wiring (Core HR / Payroll / Training writes) is a large
/// cross-cutting effort and is intentionally deferred. The default implementation
/// (<c>LogOnlyRecommendationIntegrationService</c>) emits a structured, clearly-marked log event and the
/// recommendation service writes an IMMUTABLE <c>IntegrationRaised</c> audit event, so the approval flow is
/// complete and observable and the integration is a clean seam a future story replaces. Mirrors the log-only
/// notification seams across the codebase. TODO(US-PRF integration: wire Core HR / Payroll / Training).
/// </summary>
public interface IRecommendationIntegrationService
{
    /// <summary>
    /// Raises the downstream-integration event for a freshly-approved recommendation (BR-6). Fire-and-forget; must
    /// never throw into the request path — the approval is committed even if the downstream dispatch fails.
    /// </summary>
    /// <param name="target">The downstream target label ("core-hr" | "payroll" | "training") for traceability.</param>
    Task RaiseAsync(
        Guid recommendationId,
        Guid employeeId,
        Guid cycleId,
        RecommendationType type,
        string target,
        CancellationToken cancellationToken = default);
}
