using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IPayrollNotificationService"/> that emits a structured "payroll-run-ready" log event
/// instead of dispatching a real notification (US-PAY-003 AC-3 seam). There is no notification platform yet
/// (US-NTF) and SignalR/email are deferred — the FE polls the progress query for live status. This keeps the
/// run flow complete + observable. TODO(notifications): replace with a real in-app/email dispatcher.
/// </summary>
public sealed class LogOnlyPayrollNotificationService : IPayrollNotificationService
{
    private readonly ILogger<LogOnlyPayrollNotificationService> _logger;

    public LogOnlyPayrollNotificationService(ILogger<LogOnlyPayrollNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyRunReadyForReviewAsync(
        Guid tenantId, Guid runId, int processed, int skipped, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notification event {EventType}: payroll run {RunId} ready for review (tenant {TenantId}, processed {Processed}, skipped {Skipped}).",
            "payroll-run-ready", runId, tenantId, processed, skipped);

        return Task.CompletedTask;
    }
}
