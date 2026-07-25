---
type: decision
date: 2026-07-25
status: accepted
tags: [security, multi-tenancy, sso, entra, oidc, saas]
---

# ADR — Multi-tenant SSO redirect handling: one fixed redirect URI + tenant in `state`

## Context

HRM is a multi-tenant SaaS where each customer gets a subdomain (`acme.myhrm.org`,
`globex.myhrm.org`, …). Enterprise SSO uses **one vendor-owned multi-tenant Microsoft Entra
app** (`AzureADMultipleOrgs`, `login.microsoftonline.com/organizations` authority). The recurring
question: **how do we handle the OAuth redirect URI across many tenants** — a redirect URI per
tenant? a wildcard (`*.myhrm.org`)? the subdomain in the redirect URL?

This ADR records the decision and — importantly — confirms it is **already implemented** (US-AUTH-011
/013/015). It exists so the approach isn't re-litigated. See also [[../modules/authentication-sso]].

## Decision

**One multi-tenant Entra app + ONE fixed, exact-match redirect URI for ALL tenants. The HRM tenant
subdomain is carried through the flow in the OAuth `state` parameter — NEVER in the redirect URI.**

Concretely:
- **One redirect URI**, registered verbatim: `…/api/v1/auth/sso/callback` (+ `…/api/v1/auth/sso/admin-consent/callback` for onboarding). Same URI for every tenant.
- **Tenant rides in `state`** — an **encrypted, time-limited** token (ASP.NET `ITimeLimitedDataProtector`, purpose `HRM.EntraSso.State`; a **separate** protector for admin-consent so a login state can't be replayed as a consent state). The `state` carries the subdomain + PKCE verifier + nonce + return origin.
- **Callback fans out:** the single callback validates the token, checks the token's `tid`/verified domain against **that tenant's allow-list** (fail-closed — US-AUTH-013), mints the app JWT, then returns the session to the originating subdomain (httpOnly refresh cookie + `/auth/refresh`; **no tokens in the redirect URL**).
- **Adding a tenant = an allow-list entry + a workspace, NEVER a new redirect URI.** Dev allow-list: `appsettings.Development.json` `Authentication:Entra:TenantAllowList` (keyed by subdomain). Prod: DB-backed `TenantAuthSettings` (`allowed_entra_tenant_ids` / `allowed_email_domains`, US-AUTH-012).

### Explicitly rejected
- ❌ **Per-tenant redirect URIs** — caps at 256/app and doesn't scale to a SaaS customer base.
- ❌ **Wildcard redirect URIs** (`https://*.myhrm.org/...`) — Microsoft discourages them (security), strips query/fragment on match, and they're disallowed for personal-account apps; RFC 6749 §3.1.2 wants absolute URIs.
- ❌ **Encoding the subdomain in the redirect URI** (dynamic redirect) — Entra does **exact string match** and **forbids dynamic redirect URIs**; per Microsoft they "can't be used to retain state across an authentication request — for that, use the `state` parameter."

## Why (Microsoft-grounded)

Microsoft documents this exact scenario ("several subdomains … redirect users to the page they
started from") and prescribes the **state-parameter approach**: a *shared* redirect URI that
processes the token, with the originating subdomain sent in `state`, then the app redirects onward.
The multitenant-ISV architecture guidance says the same: *"have a single Redirect URI for your
central service … validate the token and then redirect the user to their customer-specific endpoint."*
Redirect URIs are exact-match (scheme/case/path/trailing-slash), max 256, no wildcards.

## Security bar (Microsoft) → how we meet it

Microsoft warns the `state`-carrying approach risks the **open-redirector threat** (RFC 6819) unless
`state` is **encrypted or verified**, and that raw URLs/secrets shouldn't sit in `state`. Our impl:
- `state` is **encrypted + time-limited** (attacker can't forge/tamper it) and carries a **subdomain**, not an arbitrary URL; the return origin is server-issued, not attacker-supplied.
- **`tid`/domain allow-list, fail-closed** — a valid Entra user from a non-allow-listed directory is rejected (US-AUTH-013), so the `organizations` authority can't be abused for cross-tenant entry.
- **PKCE (S256)** + **nonce ↔ id_token** binding; the OAuth **authorization code is single-use** (Entra), so a replayed callback fails code exchange (`sso_token_validation_failed`, audited — [[ISSUE-328]]).
- **No tokens in the redirect URL** — session handed back via httpOnly refresh cookie + `/auth/refresh`.

## Consequences / notes

- **Prod:** register the one real redirect URI (e.g. `https://app.myhrm.org/api/v1/auth/sso/callback`) and set `GLITCHTIP_DOMAIN`-style `Platform` host accordingly. The browser must reach it.
- **Cross-subdomain session** relies on the refresh-cookie + `/auth/refresh` flow — when doing a real *multi-subdomain* login test, verify the refresh cookie's **domain scoping** reaches the SPA's API calls (base-domain-scoped, or same API host per subdomain).
- No per-tenant Entra config: onboarding a customer is an **allow-list + workspace** operation, not an Azure change.

## References

- Code: `src/backend/HRM.Infrastructure/Identity/EntraSsoService.cs`, `src/backend/HRM.Api/Controllers/SsoController.cs`; stories US-AUTH-011 / -013 / -015; complements [[ADR-2026-07-10-tenant-isolation-model]] and [[../modules/authentication-sso]].
- Microsoft: [Redirect URI restrictions + "Use a state parameter"](https://learn.microsoft.com/entra/identity-platform/reply-url) · [Dynamic redirect URIs forbidden — use `state`](https://learn.microsoft.com/entra/identity-platform/reference-breaking-changes) · [Single central redirect URI for multitenant ISVs](https://learn.microsoft.com/entra/architecture/establish-applications) · [Pass custom state (MSAL)](https://learn.microsoft.com/entra/identity-platform/msal-js-pass-custom-state-authentication-request) · [OIDC `state` round-trip](https://learn.microsoft.com/entra/identity-platform/v2-protocols-oidc)
