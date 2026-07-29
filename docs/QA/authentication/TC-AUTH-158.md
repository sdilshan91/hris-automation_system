---
id: TC-AUTH-158
user_story: US-AUTH-014
module: Authentication
priority: critical
type: security
status: draft
created: 2026-07-29
---

# TC-AUTH-158: JIT disabled — no matching membership is REJECTED fail-closed, no account or membership created

## 1. Test Objective
Verify AC-3 / AC-5 / FR-4 / FR-5 (fail-closed): when no membership matches in the resolved tenant and JIT is **not** permitted, `SsoSignInAsync` rejects the login and creates **no** user and **no** membership — it never silently provisions. This covers both sub-cases: (a) an identity with no HRM user at all (AC-5, event `sso_login_no_account`), and (b) an existing HRM user who is a member of a *different* tenant only, signing in to the resolved tenant (AC-3, event `sso_login_no_membership`) — the cross-tenant membership must NOT grant access and must NOT be auto-linked. (Implementation reality: with `identity.JitAllowed = false`, the no-user branch returns 403 "No HRM account is linked…" after writing `sso_login_no_account`; the no-membership branch returns 403 "You do not have access to this workspace." after writing `sso_login_no_membership`. In both the `EntraObjectId` link is deferred and therefore never written on a rejected attempt.)

## 2. Related Requirements
- User Story: US-AUTH-014
- Acceptance Criteria: AC-3, AC-5
- Functional Requirements: FR-4, FR-5
- Business Rules: BR-5
- Non-Functional Requirements: NFR-2 (no half-created record)

## 3. Preconditions
- **Executable via:** an xUnit arm driving `SsoSignInAsync` with a synthesized `SsoIdentity` where `JitAllowed = false`. No live IdP required for the sign-in-service assertions. (As in TC-AUTH-157, proving a tenant's `jit_enabled = false` actually yields `JitAllowed = false` in the emitted identity is the upstream `EntraSsoService` wiring — a live IdP or crafted-token `EntraSsoService` test.)
- Tenant `acme` Active. Tenant `globex` Active (for the cross-tenant sub-case).
- **Sub-case (b):** user `alice@globex.test` has an Active membership in `globex` only — none in `acme`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme | Resolved (target) tenant |
| ObjectId (no-account) | {oid_ghost} | No HRM user anywhere |
| Email (no-account) | ghost@acme.test | No HRM user anywhere |
| ObjectId (cross-tenant) | {oid_alice} | Alice's oid (member of globex only) |
| Email (cross-tenant) | alice@globex.test | Member of globex, NOT acme |
| JitAllowed | false | JIT disabled for `acme` |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **AC-5, no account:** call `SsoSignInAsync` with `Subdomain=acme`, `ObjectId={oid_ghost}`, `Email=ghost@acme.test`, `JitAllowed=false`. | Result `IsFailure`, HTTP 403, message "No HRM account is linked to this Microsoft identity." No JWT/refresh. |
| 2 | Re-count `Users` / `UserTenants` for `acme`. | **Unchanged** — no account or membership created (FR-4). |
| 3 | Assert audit. | One `sso_login_no_account` row for `acme` with a null user id; no `sso_jit_provisioned`. |
| 4 | **AC-3, other-tenant member only:** call `SsoSignInAsync` with `Subdomain=acme`, `ObjectId={oid_alice}`, `Email=alice@globex.test`, `JitAllowed=false`. | Result `IsFailure`, HTTP 403, message "You do not have access to this workspace." No JWT. |
| 5 | Re-read Alice's user row and memberships. | Alice still has her `globex` membership only; **no** `acme` membership created; her `globex` membership was NOT cross-linked or altered (FR-5, BR-5). |
| 6 | Assert audit. | One `sso_login_no_membership` row for `acme` attributed to Alice's user id; no cross-tenant grant. |
| 7 | Confirm no `oid` link mutation occurred on either rejected attempt. | For the no-account case no user exists to link; for Alice, `EntraObjectId` was not written by this denied `acme` attempt. |

## 6. Postconditions
- No account/membership was provisioned in `acme`; the cross-tenant member gained no access; each rejection is audited fail-closed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
