using System.Text.Json;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-006 Phase 2b: the real <see cref="IUserManagementNotificationService"/> (US-ADM-005). Replaces the
/// log-only stub — dispatches the invitation email (AC-2) and the admin-forced-password-reset email (AC-5) via
/// <see cref="INotificationDispatcher"/>.
///
/// <list type="bullet">
/// <item><b>Invitation</b> (<c>user_invitation</c>, SystemAnnouncements, suppressible) — the invitee has no
/// <c>User</c> row yet, so the email leg uses the dispatcher's <see cref="NotificationRequest.RecipientEmail"/>
/// raw-address override (no in-app leg). The accept link embeds the RAW one-time token; that token is never
/// logged.</item>
/// <item><b>Admin-forced reset</b> (<c>admin_password_reset</c>, SecurityAlerts, mandatory) — INFORMATIONAL only
/// (no link/token, per the product decision): it tells the user an administrator reset their password and to use
/// the self-service Forgot Password flow. Recipient is a raw email (<see cref="NotificationRequest.RecipientEmail"/>).</item>
/// </list>
///
/// <para><b>Never throws</b> — a delivery failure must not break the invitation / provisioning / force-reset flow
/// (every send is wrapped; the dispatcher is itself never-throw), preserving the LogOnly contract.</para>
/// </summary>
public sealed class RealUserManagementNotificationService : IUserManagementNotificationService
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RealUserManagementNotificationService> _logger;

    public RealUserManagementNotificationService(
        INotificationDispatcher dispatcher,
        IConfiguration configuration,
        ILogger<RealUserManagementNotificationService> logger)
    {
        _dispatcher = dispatcher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendInvitationAsync(
        UserInvitationEmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            // BUG-294/295: nested under /auth to match the Angular route table. The root-level path this
            // used to emit matched no route, so the SPA wildcard sent invitees to the login page with the
            // token discarded. Pinned by AuthEmailLinkRouteTests.
            var acceptUrl = $"{TenantBaseUrl(message.Subdomain)}/auth/accept-invite?token={message.RawToken}";
            var expiryHours = HoursUntil(message.ExpiresAt);

            var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["invitee"] = new Dictionary<string, object?> { ["email"] = message.Email },
                ["tenant"] = new Dictionary<string, object?>
                {
                    ["name"] = message.TenantName,
                    ["companyName"] = message.TenantName,
                },
                ["invitation"] = new Dictionary<string, object?>
                {
                    ["acceptUrl"] = acceptUrl,
                    ["expiryHours"] = expiryHours,
                },
            });

            // Email-only: no User row for a pending invitee → raw-address recipient, no in-app leg.
            var request = new NotificationRequest(
                TenantId: message.TenantId,
                EventKey: "user_invitation",
                PayloadJson: payloadJson,
                RecipientEmail: message.Email,
                NotificationType: "user.invitation");

            await _dispatcher.SendEmailAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never throw — invitation persistence has already committed; the raw token is NOT logged.
            _logger.LogError(ex,
                "RealUserManagementNotificationService: failed to dispatch invitation email to {Email} for tenant " +
                "'{TenantName}' ({Subdomain}).",
                message.Email, message.TenantName, message.Subdomain);
        }
    }

    public async Task SendPasswordResetAsync(
        PasswordResetEmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            // INFORMATIONAL only — no link/token (product decision). The user uses self-service Forgot Password.
            var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["user"] = new Dictionary<string, object?> { ["email"] = message.Email },
                ["tenant"] = new Dictionary<string, object?>
                {
                    ["name"] = message.Subdomain,
                    ["companyName"] = message.Subdomain,
                },
            });

            // The recipient is a real user, but the message only carries the raw email → RecipientEmail override.
            var request = new NotificationRequest(
                TenantId: message.TenantId,
                EventKey: "admin_password_reset",
                PayloadJson: payloadJson,
                RecipientEmail: message.Email,
                NotificationType: "user.password_reset.admin");

            await _dispatcher.SendEmailAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never throw — the admin force-reset (global password change + token revocation) has already committed.
            _logger.LogError(ex,
                "RealUserManagementNotificationService: failed to dispatch admin-password-reset email to {Email} " +
                "(tenant {Subdomain}).",
                message.Email, message.Subdomain);
        }
    }

    /// <summary>Builds the tenant workspace base URL (<c>https://{subdomain}.{Platform:BaseDomain}</c>).</summary>
    private string TenantBaseUrl(string subdomain)
    {
        var baseDomain = (_configuration["Platform:BaseDomain"] ?? "yourhrm.com").Trim().TrimStart('.');
        return $"https://{subdomain}.{baseDomain}";
    }

    /// <summary>Whole hours from now until <paramref name="expiresAt"/> (UTC), clamped to at least 1.</summary>
    private static int HoursUntil(DateTime expiresAt) =>
        Math.Max(1, (int)Math.Ceiling((expiresAt.ToUniversalTime() - DateTime.UtcNow).TotalHours));
}
