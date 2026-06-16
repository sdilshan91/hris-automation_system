using System.Security.Cryptography;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="ITenantWelcomeEmailService"/> (US-ADM-001 FR-4). Generates the one-time set-password
/// token (72-hour expiry) and logs the welcome email it WOULD send, treating that as a successful send —
/// mirroring the platform's other log-only notification seams (<see cref="LogOnlyPayslipEmailSender"/>, the
/// log-only <c>IPayroll/Leave/RecruitmentNotificationService</c>). There is no real transactional-email infra
/// yet (TODO US-NTF); wiring a real SMTP sender is a one-class swap.
///
/// <para>The raw set-password token is NEVER logged (only its expiry). There is no platform-wide stored
/// set-password token table today — the existing AUTH reset-password flow is itself a stub — so the token is
/// generated opaquely here and would be embedded in the set-password link of the real email. Persisting and
/// validating the token is owned by the AUTH set-password flow (TODO US-AUTH/US-NTF).</para>
/// </summary>
public sealed class LogOnlyTenantWelcomeEmailService : ITenantWelcomeEmailService
{
    private const int SetPasswordTokenLifetimeHours = 72;

    private readonly IConfiguration _configuration;
    private readonly ILogger<LogOnlyTenantWelcomeEmailService> _logger;

    public LogOnlyTenantWelcomeEmailService(
        IConfiguration configuration, ILogger<LogOnlyTenantWelcomeEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendWelcomeAsync(
        TenantWelcomeEmailMessage message, CancellationToken cancellationToken = default)
    {
        // Generate the one-time set-password token (FR-4, 72h expiry). Opaque, URL-safe, never logged raw.
        _ = GenerateOneTimeToken();
        var tokenExpiresAt = DateTime.UtcNow.AddHours(SetPasswordTokenLifetimeHours);

        var baseDomain = _configuration["Platform:BaseDomain"] ?? "yourhrm.com";
        var tenantUrl = $"https://{message.Subdomain}.{baseDomain}";
        var template = message.IsTrial ? "trial_welcome" : "active_welcome";
        var smtpHost = _configuration["Smtp:Host"];

        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            _logger.LogInformation(
                "[TENANT-WELCOME-EMAIL-STUB] Would send '{Template}' welcome email to {Recipient} for tenant " +
                "'{TenantName}' ({TenantUrl}). OwnerExists={OwnerExists}. Set-password link expires {Expiry:o}. " +
                "Configure Smtp:Host to enable real delivery.",
                template, message.OwnerEmail, message.TenantName, tenantUrl, message.OwnerExists, tokenExpiresAt);
            return Task.CompletedTask;
        }

        // TODO(US-NTF): dispatch a real SMTP/transactional welcome email when SMTP is configured.
        _logger.LogInformation(
            "Sending '{Template}' welcome email to {Recipient} for tenant '{TenantName}' ({TenantUrl}). " +
            "Set-password link expires {Expiry:o}.",
            template, message.OwnerEmail, message.TenantName, tenantUrl, tokenExpiresAt);

        return Task.CompletedTask;
    }

    private static string GenerateOneTimeToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
