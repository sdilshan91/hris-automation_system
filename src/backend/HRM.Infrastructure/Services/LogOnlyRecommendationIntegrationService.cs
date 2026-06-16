using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Log-only DOWNSTREAM-INTEGRATION seam for approved recommendations (US-PRF-010 BR-6). Emits a structured,
/// clearly-marked Serilog event instead of really writing to Core HR / Payroll / Training — the approval flow is
/// complete and observable without the cross-module wiring, which is intentionally deferred (a large cross-cutting
/// effort). Mirrors the log-only notification seams across the codebase.
/// TODO(US-PRF integration): replace with real Core HR (promotion/increment), Payroll (bonus/increment) and
/// Training (nomination) write integrations.
/// </summary>
public sealed class LogOnlyRecommendationIntegrationService : IRecommendationIntegrationService
{
    private readonly ILogger<LogOnlyRecommendationIntegrationService> _logger;

    public LogOnlyRecommendationIntegrationService(ILogger<LogOnlyRecommendationIntegrationService> logger)
        => _logger = logger;

    public Task RaiseAsync(
        Guid recommendationId, Guid employeeId, Guid cycleId, RecommendationType type, string target,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Recommendation integration SEAM] recommendation {RecommendationId} ({Type}) for employee {EmployeeId} " +
            "in cycle {CycleId} was APPROVED and should flow downstream to {Target}. Real cross-module wiring " +
            "(Core HR / Payroll / Training) deferred (US-PRF integration).",
            recommendationId, type, employeeId, cycleId, target);
        return Task.CompletedTask;
    }
}
