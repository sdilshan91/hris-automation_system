---
id: US-AUTH-013
module: Authentication & Authorization
priority: Must Have
persona: System
status: draft
created: 2026-06-21
sprint: backlog
acceptance_criteria_count: 8
---

# US-AUTH-013: Tenant-scoped `tid` / domain validation & isolation

## 1. Description
**As a** platform security measure,
**I want** every SSO login to be accepted only if the Microsoft user's Entra directory (`tid`) and/or verified email domain is allow-listed for the resolved HRM tenant,
**So that** a Microsoft user from one organization can never enter another organization's HRM workspace — preserving the platform's #1 rule of tenant isolation.

This is the security crux of the entire Entra SSO feature. Because the platform uses **one
multi-tenant Entra app** against the `organizations` authority, Microsoft will, by default,
authenticate *any* work/school user from *any* directory. Without this guard, default issuer
validation would let a user from org B complete login on tenant A.

## 2. Preconditions
- The OIDC foundation (US-AUTH-011) issues a fully validated `id_token` on the callback.
- Per-tenant SSO config (US-AUTH-012) provides `allowed_entra_tenant_ids` and/or `allowed_email_domains` for the resolved tenant.
- The HRM tenant is resolved from the `state`-carried subdomain on the callback.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The validated `id_token`'s `tid` matches one of the resolved tenant's `allowed_entra_tenant_ids` | The callback evaluates isolation | The login is permitted to proceed to user matching (US-AUTH-014). |
| AC-2 | The token's `tid` is NOT allow-listed but the user's **verified** email domain IS in `allowed_email_domains` | The callback evaluates isolation | The login is permitted (domain allow-listing as an accepted alternative). |
| AC-3 | Neither the `tid` nor the verified email domain is allow-listed for the resolved tenant | The callback evaluates isolation | The login is **rejected** with a generic error, **no** app JWT is issued, **no** JIT provisioning occurs, and an `sso_isolation_rejected` audit event is logged with the offending `tid`/domain and resolved tenant. |
| AC-4 | A user from org B authenticates against tenant A's SSO challenge | The callback evaluates isolation for tenant A | The login is rejected (B's `tid`/domain not in A's allow-list) — cross-tenant entry is impossible. |
| AC-5 | The resolved tenant has SSO enabled but an **empty** allow-list (misconfiguration) | The callback evaluates isolation | The login fails closed (rejected), never open, and logs an `sso_misconfigured` audit event. |
| AC-6 | The `id_token`'s `iss` does not correspond to a Microsoft `organizations` issuer for the presented `tid` | The custom issuer validator runs | Validation fails and the login is rejected before isolation/matching (defends against forged/cross-issuer tokens). |
| AC-7 | The email claim used for domain matching is **not** marked verified by Entra | The callback evaluates domain isolation | The unverified email is NOT used for domain allow-listing; if `tid` also fails, the login is rejected. |
| AC-8 | Isolation passes for the resolved tenant | The callback proceeds | The permitted `tid`/`oid`/email are handed to user matching scoped to the same resolved tenant; the tenant context used downstream is the resolved tenant, never one derived from the token. |

## 4. Functional Requirements
- FR-1: The system SHALL implement a **custom issuer validator** for the `organizations` authority that validates the token issuer corresponds to the presented `tid` (`https://login.microsoftonline.com/{tid}/v2.0`).
- FR-2: After token validation, the system SHALL evaluate **tenant-scoped allow-listing**: accept only if `tid ∈ allowed_entra_tenant_ids` OR (verified email domain ∈ `allowed_email_domains`) for the **resolved** HRM tenant.
- FR-3: The system SHALL **fail closed**: any failure, ambiguity, empty allow-list, or missing config results in rejection — never a default-allow.
- FR-4: The resolved HRM tenant SHALL come exclusively from the validated `state` (US-AUTH-011), never from the token's `tid`; the token's `tid` is only ever checked *against* the resolved tenant's allow-list.
- FR-5: Domain allow-listing SHALL use only the **verified** email/`upn` claim; unverified emails SHALL NOT satisfy domain checks.
- FR-6: Every isolation decision (permit/reject) SHALL be audited with tenant, `tid`, domain, `oid`, and outcome — without logging the raw token.
- FR-7: Rejections SHALL return a generic, non-enumerating error to the user (no disclosure of which check failed or whether the tenant exists).
- FR-8: This guard SHALL be the gate that allows US-AUTH-011 to be enabled in production (US-AUTH-011 BR-5).

## 5. Non-Functional Requirements
- NFR-1: The allow-list evaluation SHALL add <= 5 ms to the callback (settings cached per tenant, US-AUTH-012 NFR-1).
- NFR-2: The isolation check SHALL be impossible to bypass via parameter tampering: the resolved tenant is bound to the signed `state`, and the allow-list query is tenant-scoped at the data layer.
- NFR-3: The custom issuer validator SHALL cache OIDC metadata/keys and not weaken signature validation in exchange for multi-issuer support.
- NFR-4: Rejection response timing SHALL not leak whether a `tid`/domain is configured (avoid an enumeration oracle).

## 6. Business Rules
- BR-1: Tenant isolation is non-negotiable: an SSO login is valid **only** for the HRM tenant whose allow-list matches the user's `tid`/verified domain.
- BR-2: `tid` allow-listing is the primary check; verified-domain allow-listing is a permitted alternative, configurable per tenant.
- BR-3: An empty allow-list with SSO enabled is treated as a misconfiguration and **rejects all** logins (US-AUTH-012 BR-3 prevents reaching this state, but the runtime still fails closed).
- BR-4: The same Entra `tid`/domain MAY be allow-listed by more than one HRM tenant only if deliberately configured by each; this is an explicit per-tenant admin choice, not a default.
- BR-5: Unverified email claims are never trusted for any authorization decision.
- BR-6: The token's `tid` SHALL never be used to *select* or *switch* the HRM tenant — only to validate against the already-resolved one.

## 7. Data Requirements
- **Consumed config (from US-AUTH-012):** `allowed_entra_tenant_ids` (GUID list), `allowed_email_domains` (domain list), per resolved tenant.
- **Consumed claims:** `tid`, `oid`, verified `email`/`preferred_username`, `iss`.
- **Audit records:** `sso_isolation_rejected` (tenant, tid, domain, oid, reason), `sso_misconfigured` (tenant), `sso_issuer_invalid` (tenant, presented tid/iss).
- **No new persisted entity** beyond the US-AUTH-012 config; this story is enforcement logic.

## 8. UI/UX Notes
- User-facing: a single generic message on rejection — "Your Microsoft account isn't authorized for this workspace. Contact your administrator." No detail about which check failed.
- Tenant Admin: rejected-login attempts surface in the audit log viewer (US-NTF-005) so admins can diagnose a missing `tid`/domain in their allow-list.
- No countdown/enumeration hints; messaging is identical whether the tenant exists, SSO is off, or the allow-list misses.

## 9. Dependencies
- US-AUTH-011 (foundation) — provides the validated token + `state`-resolved tenant; this story supplies the issuer validator and gate it referenced.
- US-AUTH-012 (config) — provides the per-tenant allow-list.
- US-AUTH-007 (tenant resolution) — the resolved tenant the allow-list is checked against.
- US-AUTH-014 (matching/JIT) — runs only after isolation passes.
- US-NTF-004/005 (audit) — records every decision.

## 10. Assumptions & Constraints
- Microsoft's `tid` claim is a trustworthy, immutable directory identifier once the token signature/issuer is validated.
- Verified-email-domain matching depends on Entra emitting a verified email/`upn`; tenants relying on domain-only allow-listing accept that risk and are advised to prefer `tid`.
- The custom issuer validator is the standard mechanism for multi-tenant Entra apps; it must not be relaxed to a blanket "accept any issuer" or isolation collapses.
- This is a **Must Have** because shipping SSO without it would be a cross-tenant security breach.

## 11. Test Hints
- **Allow by tid:** Token `tid` in allow-list → login proceeds.
- **Allow by domain:** `tid` not listed, verified domain listed → proceeds.
- **Cross-tenant block:** Org-B token against tenant-A challenge → rejected + `sso_isolation_rejected`.
- **Empty allow-list:** Force runtime empty allow-list → rejected + `sso_misconfigured` (fail closed).
- **Forged/cross issuer:** Token whose `iss` doesn't match its `tid` → issuer validator rejects.
- **Unverified email:** Domain matches but email unverified → not accepted via domain; rejected if tid also misses.
- **Tenant binding:** Tamper with the callback to point at a different tenant while keeping a valid token → resolved tenant comes from signed `state`, mismatch rejected.
- **No enumeration:** Compare rejection responses/timing across (tenant absent / SSO off / allow-list miss); assert indistinguishable.
- **Multi-tid tenant:** Configure two `tid`s; assert tokens from either are accepted, a third is rejected.
