using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IImpersonationNotificationService"/> (US-ADM-003 AC-4/FR-5). Logs the tenant-admin
/// notification it WOULD send (email + in-app) when an impersonation session starts, treating that as a
/// successful dispatch — mirroring the platform's other log-only notification seams (<c>LogOnlyTenantWelcomeEmailService</c>,
/// the log-only payroll/leave/recruitment notification services). Real delivery is deferred (TODO US-NTF) and is
/// a one-class swap. Never throws, so session initiation is never blocked by a stubbed notification.
/// </summary>
public sealed class LogOnlyImpersonationNotificationService : IImpersonationNotificationService
{
    private const string NotificationTemplate = "impersonation_started";

    private readonly ILogger<LogOnlyImpersonationNotificationService> _logger;

    public LogOnlyImpersonationNotificationService(ILogger<LogOnlyImpersonationNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyImpersonationStartedAsync(
        ImpersonationStartedNotification notification, CancellationToken cancellationToken = default)
    {
        // FR-5 names both email and in-app channels; both are deferred to US-NTF. We log the message verbatim
        // (the reason is not a secret — it is intentionally surfaced to the tenant admin per BR-4).
        _logger.LogInformation(
            "[IMPERSONATION-NOTIFY-STUB] Would dispatch '{Template}' (email + in-app) to tenant '{TenantName}' " +
            "({TenantId}) admins: platform support user {Actor} started an impersonation session as {Target} at " +
            "{StartedAt:o} (read-only={ReadOnly}). Reason: {Reason}. Reference: {SessionId}. Expires {ExpiresAt:o}. " +
            "Configure a notification channel (US-NTF) to enable real delivery.",
            NotificationTemplate, notification.TargetTenantName, notification.TargetTenantId,
            notification.ImpersonatorEmail, notification.TargetUserEmail, notification.StartedAt,
            notification.IsReadOnly, notification.Reason, notification.SessionId, notification.ExpiresAt);

        return Task.CompletedTask;
    }
}
