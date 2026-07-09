using HRM.Application.Common.Interfaces;

namespace HRM.Api.Notifications;

/// <summary>
/// Minimal log-only <see cref="INotificationDispatcher"/> (US-ONB-002 NFR-3). The Notifications module
/// (US-NTF-001 in-app/SignalR, US-NTF-002 email) is not built yet, so this just records the dispatch
/// intent. The onboarding outbox + worker are real; only the final delivery hop is stubbed.
///
/// TODO(US-NTF-001/002): replace this registration with a real dispatcher that
///   - SendInAppAsync → pushes the notification through the SignalR hub to the recipient's connections;
///   - SendEmailAsync → renders the onboarding email template and sends it via the email service.
/// The outbox schema, the worker (<c>OnboardingNotificationDispatchJob</c>) and this contract stay as-is.
/// </summary>
public sealed class LoggingNotificationDispatcher : INotificationDispatcher
{
    private readonly ILogger<LoggingNotificationDispatcher> _logger;

    public LoggingNotificationDispatcher(ILogger<LoggingNotificationDispatcher> logger) => _logger = logger;

    public Task SendInAppAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "NOTIFICATION INTENT (in-app) — EventKey={EventKey}, Recipient={Recipient}, TenantId={TenantId}, Payload={Payload}",
            request.EventKey, request.RecipientUserId, request.TenantId, request.PayloadJson);
        return Task.CompletedTask;
    }

    public Task SendEmailAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "NOTIFICATION INTENT (email) — EventKey={EventKey}, Recipient={Recipient}/{Email}, TenantId={TenantId}, Payload={Payload}",
            request.EventKey, request.RecipientUserId, request.RecipientEmail, request.TenantId, request.PayloadJson);
        return Task.CompletedTask;
    }
}
