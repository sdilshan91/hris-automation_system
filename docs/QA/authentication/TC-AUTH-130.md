---
id: TC-AUTH-130
user_story: US-AUTH-016
module: Authentication
priority: high
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-130: Ordinary (non-break-glass) user with only local creds and no SSO membership is refused under `sso_only` and cannot use the break-glass path

## 1. Test Objective
Verify AC-7 / BR-2: under `sso_only`, a regular user who has only local credentials and no valid Entra SSO membership is refused via SSO-only enforcement and directed to contact their administrator. Crucially, they CANNOT escalate to the break-glass path -- break-glass is restricted to explicitly designated admin accounts (`break_glass_admin_user_ids`), not a general escape hatch.

## 2. Related Requirements
- User Story: US-AUTH-016
- Acceptance Criteria: AC-7
- Functional Requirements: FR-1, FR-2
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" is on `sso_only` with a valid SSO config and a designated break-glass admin `admin-a@acme.com`.
- `user-b@acme.com` is an ordinary user with valid LOCAL credentials but NO SSO membership (not `oid`-linked, email not in an allowed domain) and is NOT in `break_glass_admin_user_ids`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Login endpoint | POST /api/v1/auth/login | Standard + break-glass path |
| Ordinary user | user-b@acme.com / correct password | Local only, no SSO, not break-glass |
| Expected refusal | 403 + "contact your administrator" guidance | AC-7 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `user-b@acme.com`, attempt standard local `POST /api/v1/auth/login` with the correct password. | Refused (HTTP 403) via SSO-only enforcement; message directs the user to sign in with Microsoft or contact their administrator. No token issued. |
| 2 | user-b attempts the Microsoft SSO path. | SSO fails / access denied -- user-b has no valid SSO membership (fail-closed, per US-AUTH-013/014). No token issued. |
| 3 | user-b attempts the "Administrator sign-in" break-glass path with their valid local password. | REFUSED -- break-glass is restricted to designated admins (user-b's id is not in `break_glass_admin_user_ids`). Even with correct credentials, the break-glass bypass does not apply (BR-2). No token issued. |
| 4 | Confirm the designated admin is unaffected. | `admin-a@acme.com` can still use break-glass successfully (control from TC-AUTH-127) -- proving the refusal is user-specific, not a broken break-glass path. |

## 6. Postconditions
- The ordinary user has no session by any path; the break-glass restriction to designated admins holds.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
