---
id: TC-AUTH-144
user_story: US-AUTH-011
module: Authentication
priority: critical
type: security
status: draft
created: 2026-07-24
---

# TC-AUTH-144: Custom issuer validator rejects any `id_token` whose issuer does not match its own `tid`

## 1. Test Objective
Verify AC-3 / FR-5 and the custom per-`tid` issuer validation: because the multi-tenant `organizations` authority issues a per-directory issuer (`https://login.microsoftonline.com/{tid}/v2.0`), default single-issuer validation is insufficient. The `ValidateMicrosoftIssuer` hook accepts a token ONLY when its `iss` equals the templated issuer for the token's OWN `tid` claim; a mismatched or spoofed issuer is rejected fail-closed with no token.

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-3, AC-6
- Functional Requirements: FR-5

## 3. Preconditions
- Entra SSO configured. A valid signed `state` for acme exists; acme allow-lists `tid=C1`.
- A test token issuer capable of producing tokens where `iss` and `tid` can be set independently (or a signing double).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Valid token | tid=C1, iss=https://login.microsoftonline.com/C1/v2.0 | issuer matches tid |
| Mismatched token | tid=C1, iss=https://login.microsoftonline.com/OTHER/v2.0 | issuer ≠ tid template |
| Spoofed-authority token | tid=C1, iss=https://login.microsoftonline.com/common/v2.0 | wrong authority form |
| Missing-tid token | iss present, `tid` claim absent | validator cannot template |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Callback with the **valid** token (`iss` matches `tid=C1`). | Issuer validation passes; flow proceeds (allow-list then session). Positive control. |
| 2 | Callback with the **mismatched** token (`iss` for a different tid than the token's `tid`). | `ValidateMicrosoftIssuer` throws `SecurityTokenInvalidIssuerException`; overall validation fails → result `sso_failed`; NO token issued; redirect to login with `sso_error`. |
| 3 | Callback with the **spoofed-authority** token (`iss` = `.../common/v2.0`). | Rejected — does not equal `https://login.microsoftonline.com/{tid}/v2.0`. No token. |
| 4 | Callback with the **missing-tid** token. | Rejected — the validator cannot build the expected issuer and throws; no token. |
| 5 | Inspect the audit/log. | A token-validation-failed record (expected `sso_token_validation_failed`) with the failure reason (issuer), never the raw token (NFR-5). |

## 6. Postconditions
- Only tokens whose issuer matches their own `tid` (correct Microsoft per-directory issuer) pass issuer validation.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
