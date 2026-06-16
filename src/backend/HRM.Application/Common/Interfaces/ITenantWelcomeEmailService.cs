namespace HRM.Application.Common.Interfaces;

/// <summary>
/// The send seam for the tenant-owner welcome email dispatched on successful provisioning (US-ADM-001
/// FR-4 / AC-1). One message = the owner of a newly-provisioned tenant + a one-time set-password link.
///
/// <para><b>Reused infra / deferred SMTP:</b> the platform has no real transactional-email delivery yet — every
/// existing notification seam (<see cref="IPayslipEmailSender"/>, the log-only
/// <c>IPayroll/Leave/RecruitmentNotificationService</c>) is log-only. The default implementation
/// (<c>LogOnlyTenantWelcomeEmailService</c>) follows the SAME convention: it generates the one-time
/// set-password token (72-hour expiry, FR-4), logs the email it WOULD send (never the raw token) and treats
/// "logged" as a successful send. Wiring a real SMTP/transactional sender is a one-class swap (TODO US-NTF).</para>
/// </summary>
public interface ITenantWelcomeEmailService
{
    /// <summary>
    /// Dispatches the welcome email for a freshly-provisioned tenant. Returns normally on acceptance; the
    /// log-only default never throws, so provisioning is never blocked by a (stubbed) email failure
    /// (§11 partial-failure: the tenant is still created and the email can be retried).
    /// </summary>
    Task SendWelcomeAsync(TenantWelcomeEmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// The welcome email for a newly-provisioned tenant owner (US-ADM-001 FR-4). <paramref name="OwnerExists"/>
/// distinguishes a brand-new account from an existing global user being linked (AC-3 — the email "references
/// the existing account"). <paramref name="IsTrial"/> selects the trial vs. active welcome template.
/// </summary>
public sealed record TenantWelcomeEmailMessage(
    Guid TenantId,
    string TenantName,
    string Subdomain,
    string OwnerEmail,
    string? OwnerName,
    bool OwnerExists,
    bool IsTrial);
