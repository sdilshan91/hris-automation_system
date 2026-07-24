namespace HRM.Infrastructure.Identity;

/// <summary>
/// CR-AUTH-001 Increment 2 — strongly-typed Microsoft Entra (O365) SSO configuration, bound from the
/// <c>Authentication:Entra</c> config section. Secrets (<see cref="ClientSecret"/>) come from
/// user-secrets / environment, never appsettings (Critical Rule #6).
///
/// The per-tenant allow-list (<see cref="TenantAllowList"/>) is the dev-POC home for the security-critical
/// tenant-isolation config (US-AUTH-013). In the full feature this moves into per-tenant DB config
/// (US-AUTH-012); here it is keyed by HRM tenant subdomain. It is FAIL-CLOSED: a subdomain with no entry,
/// or an entry whose allow-lists are all empty, can never complete SSO.
/// </summary>
public sealed class EntraSsoOptions
{
    public const string SectionName = "Authentication:Entra";

    public bool Enabled { get; set; }

    /// <summary>OIDC authority for the multi-tenant ("organizations") endpoint, e.g.
    /// <c>https://login.microsoftonline.com/organizations/v2.0</c>. The discovery document is fetched
    /// from <c>{Authority}/.well-known/openid-configuration</c>.</summary>
    public string Authority { get; set; } = "https://login.microsoftonline.com/organizations/v2.0";

    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client secret for the code-for-token exchange. MUST come from user-secrets/Key Vault.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    public string Scopes { get; set; } = "openid profile email";

    /// <summary>The single fixed redirect URI registered on the Entra app registration. Must match the
    /// callback exactly (Microsoft rejects any unregistered URI). Dev:
    /// <c>http://localhost:5000/api/v1/auth/sso/callback</c>.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>US-AUTH-016 FR-5: the FIXED redirect URI registered for the admin-consent flow (Microsoft returns
    /// here with <c>admin_consent</c>/<c>tenant</c>/<c>state</c> or <c>error</c>). Must be registered on the app
    /// registration. Dev: <c>http://localhost:5000/api/v1/auth/sso/admin-consent/callback</c>.</summary>
    public string AdminConsentRedirectUri { get; set; } = string.Empty;

    /// <summary>Per-HRM-tenant SSO allow-list, keyed by tenant subdomain. See class remarks.</summary>
    public Dictionary<string, EntraTenantAllowList> TenantAllowList { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True only when SSO can actually complete a round-trip: enabled, with a client id AND a
    /// secret present. When false the login button reports "not configured" (fail-closed).</summary>
    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RedirectUri);

    /// <summary>US-AUTH-016: the admin-consent URL can be BUILT without the client secret (it is just a redirect
    /// to Microsoft), so this is a weaker gate than <see cref="IsConfigured"/> — enabled + client id +
    /// admin-consent redirect uri.</summary>
    public bool IsAdminConsentConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(AdminConsentRedirectUri);
}

/// <summary>
/// The SSO allow-list for one HRM tenant (US-AUTH-013). A login is permitted only when the validated
/// Entra <c>tid</c> is in <see cref="AllowedTenantIds"/> OR the user's verified email domain is in
/// <see cref="AllowedDomains"/>. Both empty ⇒ fail-closed (no entry can authenticate).
/// </summary>
public sealed class EntraTenantAllowList
{
    /// <summary>Allowed Entra directory (tenant) IDs — the <c>tid</c> claim GUIDs of the customer orgs
    /// permitted to sign in to this HRM tenant.</summary>
    public List<string> AllowedTenantIds { get; set; } = new();

    /// <summary>Allowed verified email domains (e.g. <c>contoso.com</c>).</summary>
    public List<string> AllowedDomains { get; set; } = new();

    /// <summary>When true, a first-time SSO user whose email domain is allow-listed is provisioned a
    /// membership just-in-time with <see cref="DefaultRole"/>. When false, only pre-existing members may
    /// sign in (a non-member is rejected).</summary>
    public bool JitProvisioning { get; set; }

    /// <summary>Built-in role name assigned to JIT-provisioned users (e.g. <c>Employee</c>). Required
    /// when <see cref="JitProvisioning"/> is true.</summary>
    public string? DefaultRole { get; set; }

    public bool HasAnyAllowRule => AllowedTenantIds.Count > 0 || AllowedDomains.Count > 0;
}
