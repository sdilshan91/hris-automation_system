---
id: TC-AUTH-145
user_story: US-AUTH-011
module: Authentication
priority: critical
type: security
status: blocked
created: 2026-07-24
---

# TC-AUTH-145: `id_token` validation rejects bad signature, wrong audience, expired lifetime, and nonce mismatch

## 1. Test Objective
Verify AC-3 / AC-6 / FR-5: each individual `id_token` validation defect is independently rejected — bad JWKS signature, `aud != ClientId`, expired `exp`, and `nonce != state.nonce`. In every case no application JWT is issued and a token-validation-failed audit/log record is written. (Implementation reality: signature/audience/lifetime are enforced by `TokenValidationParameters`; the nonce is checked explicitly against the state's nonce after validation.)

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-3, AC-6
- Functional Requirements: FR-5
- Non-Functional Requirements: NFR-5 (no token in logs)

## 3. Preconditions
- Entra SSO configured. A valid signed `state` for acme (nonce=N) exists; acme allow-lists `tid=C1`.
- A token-signing double / test JWKS so individual claims and the signing key can be manipulated one at a time. Each case holds all OTHER checks valid so exactly one defect is under test.

## 4. Test Data
| Case | Defect | Otherwise |
|------|--------|-----------|
| 4a | Signed with a key NOT in Microsoft JWKS (bad signature) | valid aud/iss/exp/nonce |
| 4b | `aud = some-other-client-id` (≠ ClientId) | valid signature/iss/exp/nonce |
| 4c | `exp` in the past (expired) | valid signature/aud/iss/nonce |
| 4d | `nonce = WRONG` (≠ state nonce N) | valid signature/aud/iss/exp |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Callback with case **4a** (bad signature). | `ValidateIssuerSigningKey` fails → `validation.IsValid == false` → result `sso_failed`; NO token; redirect to login with `sso_error`. |
| 2 | Callback with case **4b** (wrong audience). | Audience validation fails (`ValidAudience == ClientId`) → rejected `sso_failed`; NO token. |
| 3 | Callback with case **4c** (expired). | Lifetime validation fails (`ValidateLifetime`) → rejected `sso_failed`; NO token. |
| 4 | Callback with case **4d** (nonce mismatch). | Token validates but the explicit `nonce == state.nonce` check fails → rejected `sso_failed`; NO token (replay/session-binding protection). |
| 5 | For each case inspect the audit/log. | A token-validation-failed record (expected `sso_token_validation_failed`) with the failure reason only; the raw `id_token`/`code`/secret never appears in the log (NFR-5). |
| 6 | Positive control: submit a token valid on all four dimensions. | Passes validation and proceeds to the allow-list/session steps — confirming each rejection above is the specific injected defect. |

## 6. Postconditions
- Any single id_token validation defect blocks the login; no partial trust is granted.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
