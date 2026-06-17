using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IPayrollFnFIntegration"/> (US-ONB-005 FR-6 / BR-4 seam). Emits a structured
/// "offboarding-fnf-triggered" log event in lieu of a real cross-module call to Payroll, which does not exist
/// yet. The F&amp;F calculation is owned by Payroll (BR-4); offboarding only triggers it. Returns a stable
/// reference id for audit/correlation. TODO(payroll integration): replace with a real F&amp;F initiation call.
/// </summary>
public sealed class LogOnlyPayrollFnFIntegration : IPayrollFnFIntegration
{
    private readonly ILogger<LogOnlyPayrollFnFIntegration> _logger;

    public LogOnlyPayrollFnFIntegration(ILogger<LogOnlyPayrollFnFIntegration> logger) => _logger = logger;

    public Task<Guid> TriggerFinalSettlementAsync(
        Guid tenantId,
        Guid employeeId,
        Guid offboardingInstanceId,
        DateTime lastWorkingDay,
        CancellationToken cancellationToken = default)
    {
        var settlementRef = BaseEntity.NewUuidV7();
        _logger.LogInformation(
            "Integration event {EventType}: F&F settlement triggered for employee {EmployeeId} " +
            "(offboarding {OffboardingId}, LWD {LastWorkingDay}, ref {SettlementRef}) in tenant {TenantId}.",
            "offboarding-fnf-triggered", employeeId, offboardingInstanceId, lastWorkingDay.Date,
            settlementRef, tenantId);
        return Task.FromResult(settlementRef);
    }
}
