using HRM.Application.Features.Auth.DTOs;

namespace HRM.Application.Features.Auth;

/// <summary>
/// The outcome of the US-AUTH-013 tenant-isolation decision.
/// </summary>
/// <param name="Allowed">True only when this Entra identity may sign in to this HRM tenant.</param>
/// <param name="JitAllowed">Whether a first-time user may be provisioned just-in-time.</param>
/// <param name="DefaultRole">Role for a JIT-provisioned membership; null when JIT is not allowed.</param>
/// <param name="Reason">
/// Audit event name when denied (<c>sso_disabled_for_tenant</c> / <c>sso_misconfigured</c> /
/// <c>sso_isolation_rejected</c>); null when allowed.
/// </param>
/// <param name="DomainMatchedButUnverified">
/// True when the email domain IS allow-listed but the token asserted no verified email, so the domain rule was
/// not honoured. Surfaced so the caller can log the near-miss — it is the signal an operator needs when a user
/// who "should" be allowed is not.
/// </param>
public readonly record struct SsoIsolationDecision(
    bool Allowed,
    bool JitAllowed,
    string? DefaultRole,
    string? Reason,
    bool DomainMatchedButUnverified);

/// <summary>
/// US-AUTH-013: the pure tenant-isolation decision for Entra/OIDC sign-in — FAIL-CLOSED.
///
/// <para>Extracted as a pure function (GAP-002) for one reason: it is the control that decides whether an
/// identity from one customer's directory may enter another customer's HRM tenant, and before this it had no
/// direct test at all. It previously lived inside <c>EntraSsoService</c>, reachable only through a full OIDC
/// callback (signed state, code exchange, JWKS validation), which is why 80 passing SSO tests covered none of
/// its branches. Same "pure core, thin shell" split the payroll engine already uses.</para>
///
/// <para>The inputs are deliberately primitive — a settings snapshot plus validated claim values — so this
/// class has no dependency on HTTP, EF, caching, or the token library.</para>
/// </summary>
public static class SsoIsolationGuard
{
    /// <summary>
    /// Decides whether <paramref name="tid"/>/<paramref name="email"/> may sign in to the tenant described by
    /// <paramref name="settings"/>. Gate order is significant: a tenant that has switched SSO off is refused
    /// before any allow-list is consulted.
    /// </summary>
    /// <param name="settings">The tenant's own SSO configuration (the DB-backed snapshot, never appsettings).</param>
    /// <param name="tid">The validated Entra directory id from the id_token.</param>
    /// <param name="email">The email/UPN resolved from the id_token.</param>
    /// <param name="emailVerified">
    /// Whether the issuer asserted the email as verified (<c>xms_edov</c> / <c>email_verified</c>). Absence of
    /// the claim means "unknown" and MUST be passed as false.
    /// </param>
    public static SsoIsolationDecision Evaluate(
        SsoSettingsSnapshot settings, string tid, string email, bool emailVerified)
    {
        // (1) The tenant's own switch. Before GAP-002 no login path read this, so SsoEnabled = false did not
        // block SSO — a tenant could disable it in the UI and still have users signing in.
        if (!settings.SsoEnabled)
        {
            return new SsoIsolationDecision(false, false, null, "sso_disabled_for_tenant", false);
        }

        // (2) Enabled but unconfigured is a MISCONFIGURATION, not an attack — a distinct event (AC-5) so an
        // operator can tell "nobody set this up" apart from "someone tried to get in".
        if (settings.AllowedEntraTenantIds.Count == 0 && settings.AllowedEmailDomains.Count == 0)
        {
            return new SsoIsolationDecision(false, false, null, "sso_misconfigured", false);
        }

        var tidAllowed = settings.AllowedEntraTenantIds
            .Any(t => string.Equals(t, tid, StringComparison.OrdinalIgnoreCase));

        // (3) GAP-017 / AC-7 (FR-5): a domain match counts ONLY for a verified email. Entra does not guarantee
        // the email claim is verified; in a directory that allows unverified addresses, a user could set an
        // address at an allow-listed domain and cross into another customer's tenant on the domain rule alone.
        // The tid rule is unaffected — it is bound to the issuing directory and cannot be self-asserted.
        var domain = EmailDomain(email);
        var domainMatches = domain.Length > 0
                            && settings.AllowedEmailDomains
                                .Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));
        var domainAllowed = domainMatches && emailVerified;

        if (!tidAllowed && !domainAllowed)
        {
            return new SsoIsolationDecision(
                false, false, null, "sso_isolation_rejected", domainMatches && !emailVerified);
        }

        // JIT requires the DOMAIN rule specifically: a tid-only match must not auto-create accounts for
        // arbitrary domains inside that directory, and an unverified address must never provision one.
        var jitAllowed = settings.JitEnabled && domainAllowed;

        return new SsoIsolationDecision(
            true, jitAllowed, jitAllowed ? settings.JitDefaultRole : null,
            null, domainMatches && !emailVerified);
    }

    private static string EmailDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 && at < email.Length - 1 ? email[(at + 1)..].ToLowerInvariant() : string.Empty;
    }
}
