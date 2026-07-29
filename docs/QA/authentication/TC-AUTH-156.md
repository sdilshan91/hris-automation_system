---
id: TC-AUTH-156
user_story: US-AUTH-014
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-07-29
---

# TC-AUTH-156: First SSO sign-in bootstraps by verified email and persists the `oid` link for future logins

## 1. Test Objective
Verify AC-2 / FR-1 / FR-2 / BR-1 / BR-6 / NFR-2: when no user is linked by `oid`, `SsoSignInAsync` falls back to matching an existing membership in the **resolved tenant** by verified email, and — only after the login is fully authorized — **persists** the Entra `oid` onto that user (the account-linking step) so subsequent logins match by `oid`. The link is written transactionally with the successful sign-in; a denied attempt must never mutate the account. (Implementation reality: on an `oid` miss the code queries `Users` by lowercased `Email`, sets `needsOidLink = true`, and defers the `user.EntraObjectId = identity.ObjectId` write until the membership/authorization checks pass — the link is applied on the authorized path just before `IssueTokensAsync`, and `IdentityProvider` is set to `entra`.)

## 2. Related Requirements
- User Story: US-AUTH-014
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-2
- Business Rules: BR-1, BR-6
- Non-Functional Requirements: NFR-2 (transactional link)

## 3. Preconditions
- **Executable via:** an xUnit arm driving `SsoSignInAsync` with a synthesized `SsoIdentity` (email set, no `oid` yet stored on the target user). The live IdP round-trip is NOT required; no step here needs a live IdP.
- Tenant `acme` Active; `Employee` role seeded.
- User `employee@acme.test` exists with `EntraObjectId = null`, `IsActive = true`, and an **Active** membership in `acme` (created by a prior local flow / admin invite).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme | |
| ObjectId (oid) | {oid_new} | NOT yet stored on any user |
| Email (id_token) | employee@acme.test | Verified; matches the existing membership |
| JitAllowed | false | Proves the match uses the email path, not JIT |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm the seeded user's `EntraObjectId` is `null`. | Precondition holds — no `oid` link yet. |
| 2 | Call `SsoSignInAsync` with `Subdomain=acme`, `ObjectId={oid_new}`, `Email=employee@acme.test`, `JitAllowed=false`. | Result `IsSuccess`; app JWT + refresh issued for the existing user; no new user/membership created (matched, not provisioned). |
| 3 | Re-read the user row from the DB. | `EntraObjectId == {oid_new}` (link persisted, FR-2) and `IdentityProvider == "entra"`. |
| 4 | Assert the audit trail. | One `sso_login_succeeded` row for the matched user + `acme`. |
| 5 | **Future-login proof:** call `SsoSignInAsync` a second time with the SAME `ObjectId={oid_new}` but a deliberately *different* email string. | Now matched by `oid` (primary key) — same single user, success, still no duplicate; confirms the link is durable across logins (BR-1). |
| 6 | Re-count `Users` / `UserTenants`. | Exactly one user and one `acme` membership across both sign-ins. |

## 6. Postconditions
- The pre-existing membership now carries `entra_oid = {oid_new}`; future logins match by `oid`; both sign-ins audited.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
