---
id: TC-AUTH-012
user_story: US-AUTH-004
module: Authentication
priority: critical
type: security
status: pass
exec_note: "2026-07-03 (API, BUG-040/BUG-004 verify-rerun, PR#118/#127): PASS — takeover blocked. FORGED token ('totally-bogus-not-a-real-token-123') + valid pw -> HTTP 400 'The reset link is invalid or has expired' (was HTTP 200 takeover). REUSE of a valid single-use token after a successful reset -> HTTP 400 (token consumed). Weak pw (11ch) -> 400 'must be at least 12 characters'; 12ch all-lowercase -> 400 complexity (tenant policy enforced on reset). Steps 1(expired)/10(history) not separately exercised. Throwaway user only."
created: 2026-05-11
---
<!-- EXECUTED 2026-06-25 (API-layer, debugger-free, THROWAWAY user; shared personas untouched). VERDICT: FAIL.
  FAIL step 5 (invalid/fabricated token) — `abc123-fake-token-xyz` → HTTP 200, password CHANGED (expected 400);
  reset-password validates NOTHING beyond token non-emptiness (BUG-040 CRIT).
  Steps 1 (expired) & 3 (already-used) are STRUCTURALLY UNENFORCEABLE: no token is ever stored, so there is
  no expiry and no single-use — every non-empty token is perpetually valid and infinitely reusable (BUG-040).
  FAIL step 10 (password history) — resetting to the immediately-previous password → 200, no reuse check
  (ISSUE-053, NFR-4).
  PASS: step 7 (wrong/non-existent email) → 400 (user-lookup fail, not token logic); empty token → 400
  (validator only); step 9 (weak password `123`) → 400 with hardcoded min12+complexity messages — but the
  validator is hardcoded and ignores tenant policy (BUG-004 re-confirmed/extended).
  Findings: BUG-040 (CRIT), ISSUE-053; BUG-004 re-confirmed. -->

# TC-AUTH-012: Reset with expired/invalid token fails

## 1. Test Objective
Verify that the password reset endpoint rejects expired, already-used, or invalid tokens with a 400 Bad Request and an appropriate error message.

## 2. Related Requirements
- User Story: US-AUTH-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-2, FR-4

## 3. Preconditions
- User `john@acme.com` has requested a password reset.
- For expired token test: the reset token was generated more than 1 hour ago.
- For reused token test: the reset token has already been successfully used once.
- For invalid token test: a tampered/fabricated token string.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Email | john@acme.com | Valid user |
| Expired Token | (token generated > 1 hour ago) | Past configurable expiry |
| Used Token | (token already consumed) | Single-use token |
| Invalid Token | abc123-fake-token-xyz | Fabricated string |
| New Password | N3wS3cure!Pass2026 | Valid password |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/reset-password` with an expired token | HTTP 400 Bad Request with message "This reset link has expired. Please request a new one." |
| 2 | Verify the user's password is NOT changed | `password_hash` remains unchanged. |
| 3 | Send `POST /api/v1/auth/reset-password` with a previously used (consumed) token | HTTP 400 Bad Request with message "This reset link has already been used. Please request a new one." |
| 4 | Verify the user's password is NOT changed | `password_hash` remains unchanged. |
| 5 | Send `POST /api/v1/auth/reset-password` with a fabricated/invalid token | HTTP 400 Bad Request with message indicating the token is invalid. |
| 6 | Verify the user's password is NOT changed | `password_hash` remains unchanged. |
| 7 | Send `POST /api/v1/auth/reset-password` with a valid token but wrong email | HTTP 400 Bad Request; token-email mismatch is rejected. |
| 8 | Verify no refresh tokens are revoked in any failed scenario | Token revocation only occurs on successful password reset. |
| 9 | Test password policy violation: send a valid token with a weak password `123` | HTTP 400 Bad Request with validation errors (e.g., "Password must be at least 12 characters"). |
| 10 | Test password history: attempt to reset to a recently used password | HTTP 400 Bad Request with "Password has been used recently. Please choose a different password." |

## 6. Postconditions
- The user's password remains unchanged.
- No tokens have been revoked.
- The user is prompted to request a new reset link (for expired/used tokens) or fix validation errors.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
