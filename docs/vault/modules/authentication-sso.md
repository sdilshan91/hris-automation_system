---
module: Authentication & Authorization (Enterprise SSO)
status: draft / not implemented
---

# Enterprise SSO via Microsoft Entra ID

Spec: [[CR-AUTH-001-entra-sso]] (user-stories/authentication/CR-AUTH-001-entra-sso.md),
stories US-AUTH-011..016. **Not implemented as of 2026-06-21** — draft for human review.

## Non-obvious domain rules (get these wrong and isolation breaks)
- **ONE multi-tenant Entra app**, vendor-owned (`AzureADMultipleOrgs`), authority
  `login.microsoftonline.com/organizations`. NOT one app per customer. Customers grant
  **admin consent** once.
- **Security crux:** the `organizations` authority authenticates *any* work/school user from
  *any* directory. You MUST validate the token's `tid` (and/or verified email domain) against the
  **resolved HRM tenant's** allow-list via a **custom issuer validator**, and **fail closed**.
  Default single-issuer validation is insufficient and would allow cross-tenant entry. This is
  US-AUTH-013 (the only **Must Have** in the set).
- The resolved HRM tenant comes from a **signed, single-use `state`** (carrying the subdomain),
  **never** from the token's `tid`. `tid` is only checked *against* the resolved tenant.
- **One fixed redirect host** (`app.yourhrm.com/signin-oidc`) — wildcard subdomain redirect URIs
  aren't reliable in Entra. Carry the tenant in `state`; return tokens cross-subdomain via a
  one-time code, not a cross-site cookie.
- SSO terminates in the **existing app JWT** (`JwtService`); downstream RBAC/refresh/sessions are
  unchanged. SSO is just a new front door.
- Config lives on **`TenantAuthSettings`** (extend it — don't add a parallel store). Gated on
  `PlanFeatureFlags.Sso`.
- User matching: link Entra `oid` to an existing `user_tenant` membership (bootstrap by verified
  email, then trust `oid`). JIT provisioning is opt-in, allow-listed-only, single non-privileged
  default role.
- **Break-glass** local-admin path is mandatory before `sso_only` enforcement — never let a tenant
  lock itself out.

## Out of scope for v1 (future CRs)
SAML, SCIM auto-deprovisioning, group→role mapping, non-Microsoft OIDC IdPs, IdP-initiated login.
