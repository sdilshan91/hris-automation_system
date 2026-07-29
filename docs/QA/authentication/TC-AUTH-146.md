---
id: TC-AUTH-146
user_story: US-AUTH-011
module: Authentication
priority: high
type: security
status: blocked
created: 2026-07-24
---

# TC-AUTH-146: A validated `id_token` missing `tid`, `oid`, or verified email is rejected

## 1. Test Objective
Verify AC-6 / FR-5 and the claim-completeness guard: even after signature/audience/issuer/lifetime/nonce validation succeeds, the callback requires non-empty `tid`, `oid`, and a verified email (from `email`, falling back to a `preferred_username` containing `@`). If any of these is missing the login is rejected fail-closed with no token — the downstream user-matching/isolation cannot run on incomplete identity.

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-6
- Functional Requirements: FR-5
- Data Requirements: `tid`, `oid`, `email`/`preferred_username` consumed from the id_token

## 3. Preconditions
- Entra SSO configured. A valid signed `state` for acme exists; acme allow-list permits `tid=C1`.
- Token double able to omit individual claims while keeping the token otherwise valid (good signature/aud/iss/exp/nonce).

## 4. Test Data
| Case | Missing claim | Notes |
|------|---------------|-------|
| 4a | `tid` absent | Cannot check allow-list / issuer template |
| 4b | `oid` absent | No stable Entra object id to link the user |
| 4c | `email` absent AND `preferred_username` without `@` | No verified email to match/bootstrap |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Callback with case **4a** (no `tid`). | Rejected fail-closed (`sso_failed`); NO token. (Note: a missing `tid` also fails the issuer validator — either way, no login.) |
| 2 | Callback with case **4b** (no `oid`). | Rejected `sso_failed`; NO token. |
| 3 | Callback with case **4c** (no usable email). | Rejected `sso_failed`; NO token. |
| 4 | Positive control: token with all of `tid`/`oid`/`email` present. | Proceeds to the allow-list/session steps. |
| 5 | Inspect the audit/log. | A rejection record noting missing `tid`/`oid`/email; no raw token logged (NFR-5). |

## 6. Postconditions
- Only tokens carrying a complete, verifiable identity (`tid`+`oid`+email) can proceed to user matching.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
