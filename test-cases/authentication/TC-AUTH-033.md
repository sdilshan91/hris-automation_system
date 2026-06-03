---
id: TC-AUTH-033
user_story: US-AUTH-005
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-03
---

# TC-AUTH-033: Optional policy -- user freely enables and disables MFA

## 1. Test Objective
Verify that when a tenant's MFA policy is set to "optional," a user can freely enroll in MFA, subsequently disable it (clearing the secret and recovery codes), and re-enroll after disabling.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-6
- Functional Requirements: FR-1, FR-2, FR-8, FR-9
- Business Rules: BR-3 (negative -- disable IS allowed under optional policy)

## 3. Preconditions
- Tenant "acme" exists with `mfaPolicy = "optional"`, `mfaRequiredRoles = []`.
- User `john@acme.com` has the "Employee" role in tenant "acme" and is authenticated.
- User has `mfa_enabled = false` initially.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Employee role, no MFA initially |
| Tenant | acme | mfaPolicy=optional |
| Enroll endpoint | POST /api/v1/auth/mfa/enroll | Returns secret, QR, recovery codes |
| Verify endpoint | POST /api/v1/auth/mfa/verify | Completes enrollment |
| Disable endpoint | DELETE /api/v1/auth/mfa | Disables MFA for user |
| Audit events | mfa_enrolled, mfa_disabled | Expected audit types |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify user's initial state: `mfa_enabled = false`, no recovery codes in DB | Clean starting state. |
| 2 | Send `POST /api/v1/auth/mfa/enroll` | HTTP 200 with `{ secret, qrCodeDataUrl, recoveryCodes[] }`. |
| 3 | Generate a valid TOTP code from the returned secret | 6-digit code for the current time step. |
| 4 | Send `POST /api/v1/auth/mfa/verify` with `{ code: "{valid_code}" }` | HTTP 200 confirmation. `mfa_enabled = true`. |
| 5 | Verify `mfa_enrolled` audit event is logged | Audit record present. |
| 6 | Verify 10 recovery codes exist in `mfa_recovery_code` table | All with `used_at = null`. |
| 7 | Send `DELETE /api/v1/auth/mfa` | HTTP 200 with confirmation that MFA has been disabled. |
| 8 | Verify `users.mfa_enabled = false` in the database | MFA is deactivated. |
| 9 | Verify `users.mfa_secret` is cleared (null or empty) | TOTP secret is removed. |
| 10 | Verify all recovery codes for this user are deleted from `mfa_recovery_code` table | No orphaned codes remain. |
| 11 | Verify an `mfa_disabled` audit event is logged | Audit record contains `user_id`, `tenant_id`, timestamp. |
| 12 | Log out and log in again with the same user | Login completes without MFA challenge. Tokens are issued directly after password stage. |
| 13 | Send `POST /api/v1/auth/mfa/enroll` again (re-enrollment) | HTTP 200 with a new `{ secret, qrCodeDataUrl, recoveryCodes[] }`. New secret differs from the original. |
| 14 | Complete verification with the new secret's TOTP code | HTTP 200. `mfa_enabled = true` again with new secret and new recovery codes. |

## 6. Postconditions
- After disable: MFA is off, secret cleared, recovery codes deleted, login works without MFA.
- After re-enrollment: MFA is on with a fresh secret and fresh recovery codes.
- Audit log contains both `mfa_enrolled` and `mfa_disabled` events.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
