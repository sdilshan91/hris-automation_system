---
id: TC-AUTH-013
user_story: US-AUTH-005
module: Authentication
priority: high
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-013: Enable TOTP for user

## 1. Test Objective
Verify that a user can successfully enroll in TOTP-based MFA by generating a secret, scanning a QR code, and verifying a TOTP code, resulting in MFA being enabled on their account with recovery codes generated.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-2, AC-3
- Functional Requirements: FR-1, FR-2, FR-3, FR-4, FR-5, FR-10

## 3. Preconditions
- User `john@acme.com` is authenticated in tenant "acme".
- MFA is not currently enabled for this user (`mfa_enabled = false`).
- Tenant MFA policy is set to "optional" or "required".
- User has access to a TOTP-compatible authenticator app.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Authenticated, no MFA |
| Enroll endpoint | POST /api/v1/auth/mfa/enroll | Returns secret, QR, recovery codes |
| Verify endpoint | POST /api/v1/auth/mfa/verify | Accepts { code } |
| TOTP algorithm | SHA1, 6-digit, 30-second step | RFC 6238 compliant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/mfa/enroll` | HTTP 200 with `{ secret, qrCodeDataUrl, recoveryCodes[] }`. |
| 2 | Verify the `secret` is a valid Base32-encoded TOTP secret | Secret is properly formatted for TOTP apps. |
| 3 | Verify the `qrCodeDataUrl` contains a valid `otpauth://` URI | URI contains the correct issuer, user email, and secret. |
| 4 | Verify `recoveryCodes` contains exactly 10 codes | Array has 10 single-use recovery codes. |
| 5 | Verify the TOTP secret is stored encrypted in `users.mfa_secret` | Column value is encrypted (not plaintext). |
| 6 | Verify `mfa_enabled` is still `false` (enrollment not yet confirmed) | MFA is not active until verification. |
| 7 | Generate a valid TOTP code using the secret and a TOTP library | A 6-digit code is generated for the current time step. |
| 8 | Send `POST /api/v1/auth/mfa/verify` with `{ code: "{valid_code}" }` | HTTP 200 with confirmation message. |
| 9 | Verify `users.mfa_enabled` is now `true` | MFA is activated on the account. |
| 10 | Verify recovery codes are stored as hashed values in `mfa_recovery_code` table | 10 records exist with `code_hash` populated and `used_at = null`. |
| 11 | Verify an `mfa_enrolled` audit event is logged | Audit record contains user_id, tenant_id, timestamp. |
| 12 | Verify the QR code was generated server-side (data URL, not external service) | QR code is a base64 data URL, no external API calls. |

## 6. Postconditions
- User's `mfa_enabled` is `true` and `mfa_secret` is stored encrypted.
- 10 hashed recovery codes exist in the `mfa_recovery_code` table.
- The user will be challenged for TOTP on subsequent logins.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
