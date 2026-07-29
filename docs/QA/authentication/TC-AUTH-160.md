---
id: TC-AUTH-160
user_story: US-AUTH-014
module: Authentication
priority: high
type: security
status: draft
created: 2026-07-29
---

# TC-AUTH-160: SSO sign-in refuses a token for an inactive user, an inactive membership, or a non-active tenant

## 1. Test Objective
Verify AC-7 / FR-8 (BR from local login parity): a matched/linked user cannot obtain an app JWT via SSO when the account or its access is not in good standing — the same suspension rules as local login apply. Three fail-closed branches: (a) the matched user is globally deactivated (`IsActive = false`); (b) the matched membership is not `Active` (suspended/revoked); (c) the resolved tenant is not `Active`/`Trial` (Suspended/Terminated). (Implementation reality in `SsoSignInAsync`: `tenant.Status != Active && != Trial` → 403 "This workspace is not active."; `!user.IsActive` → 403 "Your account is inactive."; `userTenant.Status != Active` → 403 "Your access to this workspace is not active." Each exits before `IssueTokensAsync`, so no token is minted.)

## 2. Related Requirements
- User Story: US-AUTH-014
- Acceptance Criteria: AC-7
- Functional Requirements: FR-8
- Business Rules: BR-4

## 3. Preconditions
- **Executable via:** an xUnit arm driving `SsoSignInAsync` with a synthesized `SsoIdentity` for a user matched by `oid`, varying `User.IsActive`, `UserTenant.Status`, and `Tenant.Status` across arms. No live IdP required; no step here needs a live IdP.
- Roles seeded per tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme | |
| Matched by | oid | User pre-linked with `EntraObjectId` |
| Arm (a) | `User.IsActive = false` | Globally deactivated user |
| Arm (b) | `UserTenant.Status = Suspended` | Membership not Active |
| Arm (c) | `Tenant.Status = Suspended` / `Terminated` | Workspace not Active/Trial |
| JitAllowed | false | Not relevant — user already matches |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **Arm (a):** seed a user matched by `{oid}` with `IsActive = false` + an Active `acme` membership; call `SsoSignInAsync`. | Result `IsFailure`, HTTP 403, "Your account is inactive." No JWT/refresh issued. |
| 2 | **Arm (b):** seed an Active user with a **Suspended** `acme` membership; call `SsoSignInAsync`. | Result `IsFailure`, HTTP 403, "Your access to this workspace is not active." No token. |
| 3 | **Arm (c-i):** set `acme.Status = Suspended`; matched Active user + Active membership; call `SsoSignInAsync`. | Result `IsFailure`, HTTP 403, "This workspace is not active." No token. |
| 4 | **Arm (c-ii):** set `acme.Status = Terminated`; call `SsoSignInAsync`. | Result `IsFailure`, HTTP 403; no token. |
| 5 | Assert no mutation across all arms. | No `oid` re-link, no membership creation, no `sso_login_succeeded` audit row on any refused arm; the blocked attempt leaves state unchanged. |
| 6 | **Parity check:** confirm the refusal semantics match local login for the same states (suspended/inactive blocked). | SSO enforces the identical suspension rules as `LoginAsync` — SSO is authentication only, not a bypass of standing checks. |

## 6. Postconditions
- No SSO token was issued for any inactive/suspended arm; no account/membership mutated; refusals consistent with local-login rules.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
