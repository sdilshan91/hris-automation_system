using System.Globalization;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Lockout notification email service (US-AUTH-010 FR-8, NFR-3).
///
/// <para>ISSUE-063: the message CONTENT (subject + body: recipient name, lockout duration, when access is restored,
/// wait/contact instructions, and a support-contact link) is fully assembled here via <see cref="BuildContent"/>, so
/// enabling real delivery later is a one-class swap. Real SMTP delivery itself is deferred platform-wide to
/// US-NTF-006 — when <c>Smtp:Host</c> is unset (the dev/QA default) the fully-rendered content is logged instead of
/// sent; the tenant-name enrichment on the seam signature is tracked with that same delivery follow-up.</para>
/// </summary>
public sealed class LockoutNotificationService : ILockoutNotificationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LockoutNotificationService> _logger;

    public LockoutNotificationService(
        IConfiguration configuration,
        ILogger<LockoutNotificationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendLockoutNotificationAsync(
        string userEmail,
        string? displayName,
        DateTime lockedUntilUtc,
        int lockoutDurationMinutes,
        string? tenantName,
        CancellationToken cancellationToken = default)
    {
        // Support-contact link/address comes from configuration (no per-call plumbing); falls back to a sensible
        // default so the assembled body always carries a "contact support" affordance.
        var supportContact = _configuration["Support:ContactEmail"]
            ?? _configuration["Support:ContactUrl"];

        var content = BuildContent(userEmail, displayName, lockedUntilUtc, lockoutDurationMinutes, supportContact, tenantName);

        var smtpHost = _configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            // US-NTF-006: real delivery deferred. The COMPLETE content is assembled and logged (not a bare TODO), so
            // wiring an IEmailSender later is a one-line swap.
            _logger.LogWarning(
                "[LOCKOUT-EMAIL-STUB] Would send lockout notification to {Email}. Subject='{Subject}'. Body: {Body} " +
                "(Configure Smtp:Host to enable delivery.)",
                userEmail, content.Subject, content.BodyText);
            return Task.CompletedTask;
        }

        // TODO (US-NTF-006): hand `content` to the real IEmailSender. The content is already fully built above.
        _logger.LogInformation(
            "Sending lockout notification email to {Email}. Subject='{Subject}'. Locked until {LockedUntil:u}.",
            userEmail, content.Subject, lockedUntilUtc);

        return Task.CompletedTask;
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
