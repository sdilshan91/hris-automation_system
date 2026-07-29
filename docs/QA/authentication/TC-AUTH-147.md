---
id: TC-AUTH-147
user_story: US-AUTH-011
module: Authentication
priority: high
type: functional
status: blocked
created: 2026-07-24
---

# TC-AUTH-147: Code exchange failure (invalid/expired/used code, or token-endpoint error) yields no token

## 1. Test Objective
Verify AC-3 / FR-4: when the server-side authorization-code exchange fails — Microsoft rejects the code (invalid/expired/already-used), the token endpoint returns a non-2xx, the response is unparseable, or no `id_token` is present — the flow stops with no application JWT and a generic `sso_error`. The client secret is used only server-side and never surfaced.

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-3
- Functional Requirements: FR-4
- Non-Functional Requirements: NFR-5 (no code/secret in logs)

## 3. Preconditions
- Entra SSO configured. A valid signed `state` for acme exists (so the flow reaches the exchange step).
- The token endpoint (or a test double) can be made to fail per case.

## 4. Test Data
| Case | Token-endpoint behavior | Notes |
|------|-------------------------|-------|
| 4a | 400 `invalid_grant` (expired/used/invalid code) | Standard Entra rejection |
| 4b | 5xx / network error | Transient endpoint failure |
| 4c | 200 but body has no `id_token` | Malformed success |
| 4d | 200 but body is not valid JSON | Unparseable |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Callback with a valid `state` but case **4a** (Entra rejects the code). | Exchange returns null → result `sso_failed`; HTTP 302 to `/auth/login?sso_error=...`; NO JWT/refresh, no cookie. |
| 2 | Case **4b** (endpoint 5xx / network error). | Caught; result `sso_failed`; NO token. |
| 3 | Case **4c** (200 with no `id_token`). | Treated as failure; result `sso_failed`; NO token. |
| 4 | Case **4d** (200 with non-JSON body). | JSON parse guarded; result `sso_failed`; NO token. |
| 5 | Inspect the log across all cases. | The exchange failure is logged with status/reason but the `code`, `client_secret`, and any token material are NOT present in the log (NFR-5). |

## 6. Postconditions
- A failed code exchange never yields a session; secrets stay server-side.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
