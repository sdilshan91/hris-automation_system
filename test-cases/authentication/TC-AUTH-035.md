---
id: TC-AUTH-035
user_story: US-AUTH-005
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-03
---

# TC-AUTH-035: Recovery codes cannot be retrieved after enrollment

## 1. Test Objective
Verify that recovery codes are displayed exactly once during the enrollment response and are never retrievable again through any API endpoint. Confirm that stored recovery codes are hashed (not plaintext) in the database.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-2, AC-3
- Functional Requirements: FR-1, FR-5
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- User `john@acme.com` is authenticated in tenant "acme".
- User has completed MFA enrollment (per TC-AUTH-013): `mfa_enabled = true`, 10 recovery codes generated and stored.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | MFA enrolled |
| Enroll endpoint | POST /api/v1/auth/mfa/enroll | Only endpoint that returns codes |
| Profile endpoint | GET /api/v1/auth/me | User profile |
| MFA status endpoint | GET /api/v1/auth/mfa/status | MFA status (if exists) |
| DB table | mfa_recovery_code | Stores hashed codes |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Recall that during enrollment (TC-AUTH-013 step 1), `POST /api/v1/auth/mfa/enroll` returned `recoveryCodes[]` | Codes were returned exactly once in the enrollment response. |
| 2 | Send `GET /api/v1/auth/me` (user profile endpoint) | HTTP 200. Response does NOT contain `recoveryCodes`, `mfa_secret`, or any recovery code data. Only `mfa_enabled: true` status is present. |
| 3 | Send `GET /api/v1/auth/mfa/status` (if this endpoint exists) | HTTP 200 (or 404 if not implemented). If 200, response contains only `{ mfaEnabled: true, mfaMethod: "totp", recoveryCodesRemaining: 10 }` or similar summary. No plaintext codes. |
| 4 | Enumerate all documented API endpoints and verify none return recovery codes | No GET endpoint exposes recovery codes in plaintext. |
| 5 | Query `mfa_recovery_code` table directly | All 10 rows have `code_hash` values that are hashed (bcrypt/SHA-256 format), not plaintext. None match the original plaintext codes. |
| 6 | Attempt `POST /api/v1/auth/mfa/enroll` again while MFA is already enabled | HTTP 409 Conflict or HTTP 400 "MFA is already enabled. Disable first to re-enroll." Codes are not re-generated or re-displayed. |
| 7 | Verify there is no `GET /api/v1/auth/mfa/recovery-codes` or similar endpoint | HTTP 404 or endpoint does not exist in the API surface. |

## 6. Postconditions
- Recovery codes remain securely hashed in the database.
- No API endpoint exposes plaintext recovery codes after the initial enrollment response.
- Re-enrollment is blocked while MFA is active (must disable first).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
