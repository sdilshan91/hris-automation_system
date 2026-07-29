---
id: TC-AUTH-157
user_story: US-AUTH-014
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-07-29
---

# TC-AUTH-157: JIT provisioning — allow-listed user with no membership is created with `jit_default_role`

## 1. Test Objective
Verify AC-4 / FR-3 / FR-6 / BR-3 / NFR-2 / NFR-3: when no matching user/membership exists in the resolved tenant and JIT is permitted, `SsoSignInAsync` provisions a new user (for a brand-new identity) and a new **Active** `user_tenant` membership carrying the configured `jit_default_role`, links the `oid`, invalidates the user's cached my-tenants list, and issues the app JWT. (Implementation reality: `SsoSignInAsync` trusts `identity.JitAllowed` / `identity.DefaultRole` — the per-tenant `jit_enabled` / `jit_default_role` decision from PR #444 is made **upstream** in the protocol layer, `EntraSsoService.CheckIsolation`, which reads the tenant's SSO settings and only sets `JitAllowed = true` when the email domain is allow-listed. This TC pins the sign-in-service behavior; see the split-executability note below for the upstream wiring.)

## 2. Related Requirements
- User Story: US-AUTH-014
- Acceptance Criteria: AC-4
- Functional Requirements: FR-3, FR-6
- Business Rules: BR-3
- Non-Functional Requirements: NFR-2 (transactional), NFR-3 (idempotent, no duplicate)

## 3. Preconditions
- **Executable via (primary, no live IdP):** an xUnit arm calling `SsoSignInAsync` with a synthesized `SsoIdentity` where `JitAllowed = true`, `DefaultRole = "Employee"`, and an `Email`/`ObjectId` matching no existing user/membership in `acme`. Asserts the new user + membership + role + audit.
- **Needs a live IdP OR an `EntraSsoService` crafted-token unit test (one arm only):** proving that a tenant with `jit_enabled = true` and an allow-listed domain actually *produces* `JitAllowed = true` in the emitted `SsoIdentity`. That derivation lives in the protocol layer upstream of `SsoSignInAsync` and is only exercised end-to-end by a real Microsoft round-trip (or an `EntraSsoService` test fed a crafted id_token). The `SsoSignInAsync` arm alone cannot prove the tenant-settings→identity wiring.
- Tenant `acme` Active; role `Employee` seeded in `acme`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme | |
| ObjectId (oid) | {oid_jit} | No existing user |
| Email (id_token) | newhire@acme.test | Verified; allow-listed domain (upstream) |
| DisplayName | New Hire | Stored on the created user |
| JitAllowed | true | Protocol-layer allow-list decision (see split note) |
| DefaultRole | Employee | Non-privileged (ceiling enforced at config time — TC-AUTH-159) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm no user with `Email=newhire@acme.test` or `EntraObjectId={oid_jit}` and no `acme` membership exists. | Clean baseline. |
| 2 | Call `SsoSignInAsync` with `Subdomain=acme`, `ObjectId={oid_jit}`, `Email=newhire@acme.test`, `DisplayName="New Hire"`, `JitAllowed=true`, `DefaultRole="Employee"`. | Result `IsSuccess`; app JWT + refresh issued. |
| 3 | Read back the created user. | A `User` exists: `Email=newhire@acme.test`, `EntraObjectId={oid_jit}`, `IdentityProvider="entra"`, `PasswordHash == null`, `IsActive == true`. |
| 4 | Read back the membership + roles. | An **Active** `UserTenant` in `acme` with exactly one role assignment = `Employee`, `AssignedBy == "sso-jit"` (BR-3: only the configured non-privileged default role). |
| 5 | Decode the issued JWT roles claim. | Contains `Employee` only — no elevation. |
| 6 | Assert the audit trail. | A JIT-provisioning outcome is recorded (per AC-4/FR-7 the intended event is `sso_jit_provisioned`) plus the `sso_login_succeeded` success event for the new user + `acme`. *(If the current code emits only `sso_login_succeeded` on the JIT path, flag the missing `sso_jit_provisioned` event as a finding — do NOT silently pass.)* |
| 7 | **NFR-3 idempotency:** call `SsoSignInAsync` a second time with the same identity. | Matched by `oid` now — no *second* user/membership created (one identity → one membership per tenant, BR-5). |

## 6. Postconditions
- Exactly one JIT user + one Active `acme` membership with role `Employee`; `oid` linked; my-tenants cache invalidated; outcome audited.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
