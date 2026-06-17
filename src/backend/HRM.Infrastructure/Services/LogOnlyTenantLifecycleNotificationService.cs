using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="ITenantLifecycleNotificationService"/> (US-ADM-004). Logs the lifecycle notification it
/// WOULD send (email + in-app) to the tenant's billing email + Tenant Admins, treating that as a successful
/// dispatch — mirroring the platform's other log-only notification seams
/// (<c>LogOnlyImpersonationNotificationService</c>, <c>LogOnlyTenantWelcomeEmailService</c>). Real delivery is
/// deferred (TODO US-NTF) and is a one-class swap. Never throws, so a lifecycle transition is never blocked by
/// a stubbed notification.
/// </summary>
public sealed class LogOnlyTenantLifecycleNotificationService : ITenantLifecycleNotificationService
{
    private readonly ILogger<LogOnlyTenantLifecycleNotificationService> _logger;

    public LogOnlyTenantLifecycleNotificationService(ILogger<LogOnlyTenantLifecycleNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyLifecycleChangeAsync(
        TenantLifecycleNotification notification, CancellationToken cancellationToken = default)
    {
        var recipients = new List<string>();
        if (!string.IsNullOrWhiteSpace(notification.BillingEmail))
            recipients.Add(notification.BillingEmail!);
        recipients.AddRange(notification.AdminEmails);

        _logger.LogInformation(
            "[TENANT-LIFECYCLE-NOTIFY-STUB] Would dispatch '{EventType}' (email + in-app) to tenant " +
            "'{TenantName}' ({Subdomain}) recipients [{Recipients}]. Reason: {Reason}. " +
            "TerminationScheduledAt: {ScheduledAt}. Configure a notification channel (US-NTF) to enable real delivery.",
            notification.EventType, notification.TenantName, notification.TenantSubdomain,
            string.Join(", ", recipients.Distinct()), notification.Reason ?? "(none)",
            notification.TerminationScheduledAt?.ToString("o") ?? "(n/a)");

        return Task.CompletedTask;
    }
}
