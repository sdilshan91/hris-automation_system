using System.Globalization;
using System.Net;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Lockout notification email service (US-AUTH-010 FR-8, NFR-3).
///
/// <para>ISSUE-063: the message CONTENT (subject + body: recipient name, lockout duration, when access is restored,
/// wait/contact instructions, and a support-contact link) is fully assembled here via <see cref="BuildContent"/>.
/// DF-40: the assembled content is now handed to the generic <see cref="IEmailSender"/> seam for real delivery.
/// The injected sender owns the SMTP-vs-log-only decision (<c>LogOnlyEmailSender</c> when <c>Smtp:Host</c> is unset,
/// <c>SmtpEmailSender</c> otherwise), so this service no longer inspects <c>Smtp:Host</c> itself. This method runs as
/// a Hangfire job (<c>Enqueue&lt;ILockoutNotificationService&gt;</c>), so send failures are allowed to propagate to
/// trigger a Hangfire retry.</para>
/// </summary>
public sealed class LockoutNotificationService : ILockoutNotificationService
{
    private readonly IConfiguration _configuration;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<LockoutNotificationService> _logger;

    public LockoutNotificationService(
        IConfiguration configuration,
        IEmailSender emailSender,
        ILogger<LockoutNotificationService> logger)
    {
        _configuration = configuration;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task SendLockoutNotificationAsync(
        string userEmail,
        string? displayName,
        DateTime lockedUntilUtc,
        int lockoutDurationMinutes,
        string? tenantName,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        // Support-contact link/address comes from configuration (no per-call plumbing); falls back to a sensible
        // default so the assembled body always carries a "contact support" affordance.
        var supportContact = _configuration["Support:ContactEmail"]
            ?? _configuration["Support:ContactUrl"];

        var content = BuildContent(userEmail, displayName, lockedUntilUtc, lockoutDurationMinutes, supportContact, tenantName);

        // DF-40: hand the fully-rendered content to the generic email seam. The injected sender decides SMTP vs
        // log-only (never a hard dependency), so no Smtp:Host inspection here. Exceptions propagate on purpose —
        // this runs as a Hangfire job, so a transient send failure becomes a retry (delivery durability).
        var message = new EmailMessage(
            TenantId: tenantId ?? Guid.Empty,
            RecipientEmail: userEmail,
            Subject: content.Subject,
            BodyHtml: BuildHtmlBody(content.BodyText),
            BodyText: content.BodyText);

        await _emailSender.SendAsync(message, cancellationToken);

        _logger.LogInformation(
            "Lockout notification dispatched to {Email}. Subject='{Subject}'. Locked until {LockedUntil:u}.",
            userEmail, content.Subject, lockedUntilUtc);
    }

    /// <summary>
    /// DF-40: minimal HTML wrapper for the plain-text body — HTML-encodes the text and preserves paragraph breaks so
    /// the same content renders in HTML-only mail clients. No new copy is introduced (subject/body stay in <see cref="BuildContent"/>).
    /// </summary>
    private static string BuildHtmlBody(string bodyText)
    {
        var encoded = WebUtility.HtmlEncode(bodyText).Replace("\n", "<br />\n");
        return $"<html><body><p>{encoded}</p></body></html>";
    }

    /// <summary>
    /// ISSUE-063: assembles the complete lockout-notification content (subject + plain-text body) from the seam
    /// payload. Pure + deterministic so the FR-8 Data-Requirements fields (recipient name, lockout duration, restore
    /// time, wait/contact instructions, support link) are unit-testable without touching SMTP.
    /// </summary>
    internal static LockoutEmailContent BuildContent(
        string userEmail,
        string? displayName,
        DateTime lockedUntilUtc,
        int lockoutDurationMinutes,
        string? supportContact,
        string? tenantName = null)
    {
        var greetingName = string.IsNullOrWhiteSpace(displayName) ? userEmail : displayName!;
        var restoreUtc = lockedUntilUtc.ToUniversalTime().ToString("f", CultureInfo.InvariantCulture) + " UTC";
        var support = string.IsNullOrWhiteSpace(supportContact) ? "your system administrator" : supportContact!;
        // ISSUE-063: brand the opening line + sign-off with the tenant name when the login-time tenant resolved;
        // degrades to the generic "your account" / "The HRM Team" wording when it is unknown.
        var hasTenant = !string.IsNullOrWhiteSpace(tenantName);
        var accountClause = hasTenant ? $"Your {tenantName!.Trim()} account" : "Your account";
        var signOff = hasTenant ? $"The {tenantName!.Trim()} Team" : "The HRM Team";

        const string subject = "Your account has been temporarily locked";

        var body =
            $"Hello {greetingName},\n\n" +
            $"{accountClause} has been temporarily locked following several unsuccessful sign-in attempts.\n\n" +
            $"For your security, access is suspended for {lockoutDurationMinutes} minute(s). " +
            $"You can try signing in again after {restoreUtc}.\n\n" +
            "If this wasn't you, or you need to regain access sooner, please reset your password or " +
            $"contact {support} for assistance.\n\n" +
            $"Regards,\n{signOff}";

        return new LockoutEmailContent(subject, body);
    }
}

/// <summary>ISSUE-063: rendered lockout-notification content (subject + plain-text body) built from the seam payload.</summary>
public sealed record LockoutEmailContent(string Subject, string BodyText);
