---
id: TC-AUTH-155
user_story: US-AUTH-014
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-07-29
---

# TC-AUTH-155: Existing user linked by Entra `oid` is matched on sign-in — no duplicate, no role change

## 1. Test Objective
Verify AC-1 / AC-6 / FR-1 / BR-1 / BR-4: an SSO sign-in for a user whose membership is already linked to their Entra `oid` matches by `oid` (the primary, authoritative key), loads the existing membership + roles, issues the app JWT for that account, and creates **no** duplicate user/membership. The `oid` link is trusted even when a *different* verified email now arrives for the same `oid` (AC-6), and SSO never elevates the matched user's roles (BR-4). (Implementation reality: `AuthService.SsoSignInAsync` first queries `Users` by `EntraObjectId == identity.ObjectId` with `IgnoreQueryFilters()`; a hit short-circuits the email-bootstrap and JIT branches and flows straight to `IssueTokensAsync` with the membership's existing role graph — no `EntraObjectId`/role mutation on this path.)

## 2. Related Requirements
- User Story: US-AUTH-014
- Acceptance Criteria: AC-1, AC-6
- Functional Requirements: FR-1
- Business Rules: BR-1, BR-4, BR-5
- Non-Functional Requirements: NFR-1 (indexed `oid` lookup), NFR-3 (no duplicate)

## 3. Preconditions
- **Executable via:** an xUnit arm that constructs `AuthService` over EF InMemory/Testcontainers and calls `SsoSignInAsync` with a **synthesized `SsoIdentity`** (mirrors `SsoFailureAuditWriteTests.SsoSignIn_Success_Writes_SsoLoginSucceeded_NotLegacyName`). The live IdP round-trip is NOT required — by contract an `SsoIdentity` already passed OIDC validation + isolation, so this arm bypasses the Microsoft leg entirely. No step here needs a live IdP.
- Tenant `acme` is Active; role `Employee` seeded in `acme`.
- User `user@acme.test` exists with `EntraObjectId = {oid_A}`, `IsActive = true`, `PasswordHash = null`, and an **Active** `Employee` membership in `acme`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme | Resolved from the signed OIDC state |
| ObjectId (oid) | {oid_A} | Already stored on the user |
| Email (id_token) | user@acme.test | Matches the stored user |
| Changed email (AC-6) | user.new@acme.test | A *different* verified email for the SAME `oid_A` |
| JitAllowed | false | Irrelevant — an `oid` match never reaches the JIT branch |
| DefaultRole | Employee | Must NOT be (re)assigned on a match |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Snapshot the row counts: `Users`, `UserTenants`, `UserTenantRoles` for `acme`. | Baseline captured (1 user, 1 membership, 1 role assignment). |
| 2 | Call `SsoSignInAsync` with `Subdomain=acme`, `ObjectId={oid_A}`, `Email=user@acme.test`, `JitAllowed=false`. | Result `IsSuccess`; a non-empty `AccessToken` + refresh issued for `user@acme.test`. |
| 3 | Re-count `Users` / `UserTenants` / `UserTenantRoles`. | **Unchanged** from step 1 — no duplicate user, membership, or role row created (NFR-3). |
| 4 | Decode the issued JWT's roles claim. | Exactly the pre-existing role set (`Employee`) — no roles added/removed by the SSO sign-in (BR-4). |
| 5 | **AC-6:** call `SsoSignInAsync` again with the SAME `ObjectId={oid_A}` but `Email=user.new@acme.test` (a changed verified email). | Matched by `oid` (authoritative); still success for the same single user; no second account created. The stored email may be refreshed but the account identity is unchanged. |
| 6 | Assert the audit trail. | Each successful sign-in wrote one `sso_login_succeeded` row attributed to the matched `user.Id` + `acme` tenant; no `sso_jit_provisioned`/`sso_login_no_membership` rows. |

## 6. Postconditions
- Exactly one user + one `acme` membership exist for `{oid_A}`; roles unchanged; each sign-in audited as `sso_login_succeeded`.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
