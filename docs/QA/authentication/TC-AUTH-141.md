---
id: TC-AUTH-141
user_story: US-AUTH-011
module: Authentication
priority: high
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-141: Expired `state` (past its 10-minute lifetime) is rejected fail-closed

## 1. Test Objective
Verify AC-5 / NFR-3: a signed `state` that is presented after its lifetime has elapsed is rejected — the time-limited protector refuses to unprotect an expired blob, no token is issued, and a state-invalid audit/log record is written. (Implementation reality: `StateLifetimeMinutes = 10`; the `ITimeLimitedDataProtector` enforces expiry.)

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-3 (single-use, time-limited)

## 3. Preconditions
- Entra SSO configured. A valid signed `state` for acme was generated at time T0 (state lifetime = 10 minutes).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| State age at callback | 11 minutes (> 10-minute lifetime) | Boundary just past expiry |
| Callback | GET /api/v1/auth/sso/callback?code={valid}&state={expired} | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Present the `state` at T0 + 11 min (past lifetime) with a valid `code`. | `Unprotect` fails on expiry → result `sso_failed`; HTTP 302 to `/auth/login?sso_error=...`; NO code exchange, NO JWT/refresh. |
| 2 | (Boundary) Present a fresh `state` well within the window (e.g. T0 + 1 min) with an otherwise-valid flow. | Accepted at the state stage (the flow proceeds to code exchange / token validation) — confirming the rejection in step 1 is the expiry, not an unrelated failure. |
| 3 | Inspect the audit log. | A state-invalid record (expected `sso_state_invalid`) with the expiry reason; no token/secret material logged (NFR-5). |

## 6. Postconditions
- Expired states cannot be redeemed; only within-lifetime states proceed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
