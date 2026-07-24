---
id: TC-AUTH-140
user_story: US-AUTH-011
module: Authentication
priority: critical
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-140: Tampered, missing, or replayed `state` is rejected fail-closed with no token

## 1. Test Objective
Verify AC-5 / FR-3 / NFR-3: a callback whose `state` is tampered with, forged, or missing is rejected before any token exchange — no application JWT is issued, the browser is returned to the login page with a generic `sso_error`, and a state-invalid audit/log record is written. (Implementation reality: the signed `state` is an ASP.NET Data-Protection time-limited blob; any modification, forgery, or purpose mismatch fails `Unprotect` and returns `sso_failed` fail-closed. The AC's `sso_state_invalid` audit event is the expected outcome record.)

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-5
- Functional Requirements: FR-3
- Non-Functional Requirements: NFR-3 (single-use, replay detectable), NFR-5 (no token/secret in logs)

## 3. Preconditions
- Entra SSO configured. A valid signed `state` for acme exists from a challenge.
- The Microsoft token endpoint is reachable (to prove the flow is stopped BEFORE any exchange).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Callback endpoint | GET /api/v1/auth/sso/callback?code={valid}&state={...} | |
| Tampered state | valid state with one character flipped | Breaks the Data-Protection signature |
| Forged state | attacker-crafted base64 blob | Not produced by our protector |
| Missing state | state omitted entirely | |
| Replayed state | a state from a DIFFERENT protector purpose (e.g. an admin-consent state) | Purpose isolation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Callback with a **tampered** `state` (one byte flipped) + a valid-looking `code`. | Rejected fail-closed: `Unprotect` fails, result `sso_failed`; HTTP 302 to `/auth/login?sso_error=...`; NO code exchange occurs, NO JWT/refresh issued, no `Set-Cookie: refreshToken`. |
| 2 | Callback with a **forged** `state` (attacker-crafted blob). | Same rejection — the forged blob cannot be unprotected. No token. |
| 3 | Callback with **no** `state` (only `code`). | Rejected `sso_failed` (code+state both required). No token. |
| 4 | Callback with a `state` protected under a DIFFERENT purpose (e.g. the admin-consent protector). | Rejected — the login-flow protector cannot unprotect a differently-purposed blob (a used/foreign state cannot be replayed into the login flow). No token. |
| 5 | Inspect the audit log / Serilog for each attempt. | A state-invalid record is written (AC-5, expected event `sso_state_invalid`) with the reason — and NO `id_token`, `code`, or secret material appears in the log (NFR-5). |

## 6. Postconditions
- No session was created for any tampered/missing/forged/replayed state; each was audited.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
