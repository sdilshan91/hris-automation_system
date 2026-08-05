using System.Text.Json;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-006 Phase 2b: the real <see cref="ITenantWelcomeEmailService"/> (US-ADM-001 FR-4). Replaces the log-only
/// stub — dispatches the tenant-owner welcome email via <see cref="INotificationDispatcher"/>.
///
/// <para>INFORMATIONAL only: there is NO set-password token/link (per the product decision). A not-yet-existing
/// owner is pointed at the self-service Forgot Password page to set their first password. The trial vs. active
/// workspace selects the catalog event (<c>tenant_welcome_trial</c> / <c>tenant_welcome_active</c>,
/// SystemAnnouncements, suppressible).</para>
///
/// <para>The recipient is the raw owner email (<see cref="NotificationRequest.RecipientEmail"/>): a freshly-created
/// owner may not have a signed-in user session, so this is email-only (no in-app leg). <b>Never throws</b> — a
/// delivery failure must not block provisioning (§11 partial-failure), preserving the LogOnly contract.</para>
/// </summary>
public sealed class RealTenantWelcomeEmailService : ITenantWelcomeEmailService
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RealTenantWelcomeEmailService> _logger;

    public RealTenantWelcomeEmailService(
        INotificationDispatcher dispatcher,
        IConfiguration configuration,
        ILogger<RealTenantWelcomeEmailService> logger)
    {
        _dispatcher = dispatcher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendWelcomeAsync(
        TenantWelcomeEmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var baseDomain = (_configuration["Platform:BaseDomain"] ?? "yourhrm.com").Trim().TrimStart('.');
            var forgotPasswordUrl = $"https://{message.Subdomain}.{baseDomain}/auth/forgot-password";
            var eventKey = message.IsTrial ? "tenant_welcome_trial" : "tenant_welcome_active";

            var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["owner"] = new Dictionary<string, object?> { ["name"] = message.OwnerName },
                ["subdomain"] = message.Subdomain,
                ["tenant"] = new Dictionary<string, object?>
                {
                    ["name"] = message.TenantName,
                    ["companyName"] = message.TenantName,
                },
                ["forgotPassword"] = new Dictionary<string, object?> { ["url"] = forgotPasswordUrl },
            });

            // Email-only: a brand-new owner may have no session yet → raw-address recipient, no in-app leg.
            var request = new NotificationRequest(
                TenantId: message.TenantId,
                EventKey: eventKey,
                PayloadJson: payloadJson,
                RecipientEmail: message.OwnerEmail,
                NotificationType: $"tenant.welcome.{(message.IsTrial ? "trial" : "active")}");

            await _dispatcher.SendEmailAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never throw — the tenant is already provisioned; a welcome-email failure must not surface (§11).
            _logger.LogError(ex,
                "RealTenantWelcomeEmailService: failed to dispatch welcome email to {Recipient} for tenant " +
                "'{TenantName}' ({Subdomain}).",
                message.OwnerEmail, message.TenantName, message.Subdomain);
        }
    }
}
