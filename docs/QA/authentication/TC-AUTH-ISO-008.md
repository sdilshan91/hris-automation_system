---
id: TC-AUTH-ISO-008
user_story: US-AUTH-014
module: Authentication
priority: critical
type: security
status: draft
created: 2026-07-29
---

# TC-AUTH-ISO-008: SSO match/link/JIT is strictly tenant-scoped — a user matched/created under tenant A is never visible or linkable from tenant B

## 1. Test Objective
Verify FR-5 / BR-2 / BR-5: the entire SSO match → link → JIT surface is scoped to the **resolved** tenant, so one Microsoft identity's presence in tenant A never grants access to, links into, or leaks a membership in tenant B. Concretely: (1) a membership/`oid` link in A does NOT satisfy a sign-in to B — B either JIT-provisions an independent membership (if allow-listed) or is rejected fail-closed (if not), never reusing A's membership; (2) JIT under B creates a *separate* membership, and the same person ends up with two independent linked memberships (BR-2); (3) the `(tenant_id, oid)` uniqueness is per tenant (BR-5). (Implementation reality: `SsoSignInAsync` resolves the tenant from `identity.Subdomain`, then does every membership/role query with `IgnoreQueryFilters()` **plus an explicit `TenantId == tenant.Id` predicate** — the `oid`/email user lookup is global by design, but the *authorization* pivot is the per-tenant `UserTenant`, so a foreign-tenant membership can never authorize the resolved tenant.)

## 2. Related Requirements
- User Story: US-AUTH-014
- Acceptance Criteria: AC-3
- Functional Requirements: FR-5
- Business Rules: BR-2, BR-5
- Non-Functional Requirements: NFR-3 (unique `(tenant_id, oid)`)

## 3. Preconditions
- **Executable via:** an xUnit two-tenant arm driving `SsoSignInAsync` against subdomain A then subdomain B with synthesized `SsoIdentity` values for the same `oid`/email. No live IdP required; no step here needs a live IdP. (A Playwright cross-tenant arm would additionally need a real Microsoft round-trip and is out of scope for this authored TC.)
- Two Active tenants: `acme` (A) and `globex` (B), each with `Employee` seeded.
- User `carol@shared.test` has an Active `acme` membership linked to `EntraObjectId = {oid_carol}`. She has **no** `globex` membership.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Carol is a member here |
| Tenant B | globex | Carol is NOT a member here |
| ObjectId (oid) | {oid_carol} | Linked to Carol in acme |
| Email | carol@shared.test | Same verified email in both tenants' id_tokens |
| B JitAllowed (arm 1) | false | globex does not allow-list Carol's domain |
| B JitAllowed (arm 2) | true | globex allow-lists Carol's domain |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **A→B no leak, JIT off:** call `SsoSignInAsync` with `Subdomain=globex`, `ObjectId={oid_carol}`, `Email=carol@shared.test`, `JitAllowed=false`. | Result `IsFailure`, HTTP 403 "You do not have access to this workspace." — A's membership/`oid` link does NOT authorize B (FR-5). |
| 2 | Re-read Carol's memberships. | Still exactly one membership (in `acme`); no `globex` membership was created or cross-linked; `sso_login_no_membership` audited under `globex`. |
| 3 | **A→B independent JIT, JIT on:** call `SsoSignInAsync` with `Subdomain=globex`, same `oid`/email, `JitAllowed=true`, `DefaultRole="Employee"`. | Success — a **new, independent** `globex` membership is JIT-provisioned with `Employee`; the `acme` membership is untouched. |
| 4 | Re-read Carol's memberships + roles. | Two independent memberships: `acme` (original roles) and `globex` (`Employee` only). Roles do NOT leak across tenants (BR-2) — her `acme` roles are not copied into `globex`. |
| 5 | **Uniqueness scope:** inspect `(tenant_id, oid)` rows. | `{oid_carol}` maps to at most one membership *per tenant* — one in `acme`, one in `globex` — proving the uniqueness constraint is tenant-scoped, not global (BR-5). |
| 6 | **DB-level isolation:** with the query filter active for `globex` context, read `UserTenants`. | Only `globex` membership rows are visible; `acme`'s membership row is invisible under a `globex`-scoped context (global query filter on `TenantId`; RLS deferred per platform reality). |
| 7 | **Cache isolation:** inspect the my-tenants cache after the JIT in step 3. | Invalidation is keyed to `user.Id`; Carol's refreshed my-tenants list reflects both memberships correctly with no cross-tenant role bleed. |

## 6. Postconditions
- Carol holds two independent, separately-linked memberships; A's membership never authorized or leaked into B; uniqueness is per `(tenant_id, oid)`.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
