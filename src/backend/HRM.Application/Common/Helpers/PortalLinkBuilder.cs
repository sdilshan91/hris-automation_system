namespace HRM.Application.Common.Helpers;

/// <summary>
/// Builds the anonymous candidate-portal magic-link URL (US-REC-008 FR-7) — the single source of truth for the
/// FE contract <c>https://{subdomain}.{baseDomain}/portal?token={rawToken}</c>. Reused by every emitter that
/// embeds a portal link: the applicant status-tracking email (DF-41) and the offer email (DF-42). Mirrors the
/// tenant base-URL precedent in <c>RealUserManagementNotificationService.TenantBaseUrl</c>.
///
/// <para>The raw token minted by <c>IApplicantPortalTokenService.IssueAsync</c> is base64url (URL-safe: only
/// <c>-</c>/<c>_</c>/<c>.</c>, no padding), so it is embedded verbatim — no percent-encoding needed.</para>
/// </summary>
public static class PortalLinkBuilder
{
    /// <summary>The anonymous candidate-portal route the FE serves (consumes <c>?token=</c>).</summary>
    public const string PortalPath = "/portal";

    /// <summary>Fallback platform base domain when <c>Platform:BaseDomain</c> is not configured.</summary>
    public const string DefaultBaseDomain = "yourhrm.com";

    /// <summary>
    /// Normalises a configured <c>Platform:BaseDomain</c> value (falling back to <see cref="DefaultBaseDomain"/>,
    /// trimming whitespace + a leading dot) exactly as the tenant base-URL precedent does. The signing secret /
    /// base domain are never hardcoded — callers pass <c>_configuration["Platform:BaseDomain"]</c>.
    /// </summary>
    public static string NormalizeBaseDomain(string? configuredBaseDomain)
        => (configuredBaseDomain ?? DefaultBaseDomain).Trim().TrimStart('.');

    /// <summary>
    /// Builds <c>https://{subdomain}.{baseDomain}/portal?token={rawToken}</c>. <paramref name="baseDomain"/> is
    /// already normalised (see <see cref="NormalizeBaseDomain"/>); <paramref name="rawToken"/> is the raw signed
    /// token to embed.
    /// </summary>
    public static string Build(string subdomain, string baseDomain, string rawToken)
        => $"https://{subdomain}.{baseDomain}{PortalPath}?token={rawToken}";
}
