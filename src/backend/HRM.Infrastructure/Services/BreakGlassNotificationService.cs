using System.Globalization;
using System.Net;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-AUTH-016 FR-4/BR-4 (NFR-2): break-glass security-alert email service. Runs as a Hangfire job
/// (<c>Enqueue&lt;IBreakGlassNotificationService&gt;</c>) so the alert is delivered within 60s of a break-glass
/// login, off the login request path. Assembles the content here and hands it to the generic
/// <see cref="IEmailSender"/> seam (which owns the SMTP-vs-log-only decision), so this is never a hard dependency.
/// Send failures propagate on purpose so Hangfire retries (delivery durability). Mirrors
/// <see cref="LockoutNotificationService"/>.
/// </summary>
public sealed class BreakGlassNotificationService : IBreakGlassNotificationService
{
    private readonly IConfiguration _configuration;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<BreakGlassNotificationService> _logger;

    public BreakGlassNotificationService(
        IConfiguration configuration,
        IEmailSender emailSender,
        ILogger<BreakGlassNotificationService> logger)
    {
        _configuration = configuration;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task SendBreakGlassAlertAsync(
        Guid tenantId,
        string? tenantName,
        Guid adminUserId,
        string adminEmail,
        string? adminDisplayName,
        string? sourceIp,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        // The security-alert recipient(s): a configured security contact, else the admin who signed in (so the
        // legitimate owner of the break-glass account is at minimum informed the account was used).
        var recipient = _configuration["Support:SecurityContactEmail"]
            ?? _configuration["Support:ContactEmail"]
            ?? adminEmail;

        var content = BuildContent(tenantName, adminEmail, adminDisplayName, sourceIp, occurredAtUtc);

        var message = new EmailMessage(
            TenantId: tenantId == Guid.Empty ? Guid.Empty : tenantId,
            RecipientEmail: recipient,
            Subject: content.Subject,
            BodyHtml: BuildHtmlBody(content.BodyText),
            BodyText: content.BodyText);

        await _emailSender.SendAsync(message, cancellationToken);

        _logger.LogWarning(
            "Break-glass login alert dispatched for admin {AdminUserId} ({AdminEmail}) in tenant {TenantId} from {SourceIp} at {OccurredAt:u}.",
            adminUserId, adminEmail, tenantId, sourceIp ?? "unknown", occurredAtUtc);
    }

    private static string BuildHtmlBody(string bodyText)
    {
        var encoded = WebUtility.HtmlEncode(bodyText).Replace("\n", "<br />\n");
        return $"<html><body><p>{encoded}</p></body></html>";
    }

    /// <summary>
    /// Assembles the break-glass alert content (subject + plain-text body). Pure + deterministic so the
    /// FR-4 data fields (tenant, admin user, timestamp, source IP) are unit-testable without touching SMTP.
    /// </summary>
    internal static BreakGlassEmailContent BuildContent(
        string? tenantName,
        string adminEmail,
        string? adminDisplayName,
        string? sourceIp,
        DateTime occurredAtUtc)
    {
        var who = string.IsNullOrWhiteSpace(adminDisplayName) ? adminEmail : $"{adminDisplayName} ({adminEmail})";
        var whenUtc = occurredAtUtc.ToUniversalTime().ToString("f", CultureInfo.InvariantCulture) + " UTC";
        var ip = string.IsNullOrWhiteSpace(sourceIp) ? "an unknown address" : sourceIp!;
        var org = string.IsNullOrWhiteSpace(tenantName) ? "your organization" : tenantName!.Trim();

        const string subject = "Security alert: break-glass administrator sign-in";

        var body =
            $"A break-glass (emergency local) administrator sign-in was used for {org}.\n\n" +
            $"Administrator: {who}\n" +
            $"When: {whenUtc}\n" +
            $"Source IP: {ip}\n\n" +
            "Break-glass sign-in bypasses SSO enforcement and is reserved for emergency administrator access. " +
            "If you recognize this activity, no action is needed. If you do NOT recognize it, treat it as a " +
            "potential compromise: review the audit log, rotate the administrator's password, and contact your " +
            "security team immediately.";

        return new BreakGlassEmailContent(subject, body);
    }
}

/// <summary>US-AUTH-016: rendered break-glass alert content (subject + plain-text body) built from the seam payload.</summary>
public sealed record BreakGlassEmailContent(string Subject, string BodyText);
