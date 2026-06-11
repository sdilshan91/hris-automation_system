---
id: TC-AUTH-097
user_story: US-AUTH-010
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-097: Timing-attack resistance -- locked vs non-existent accounts have similar response times

## 1. Test Objective
Verify that the response time for a locked account login attempt is indistinguishable from the response time for a non-existent account login attempt (NFR-4). An attacker should not be able to determine whether an account is locked by measuring response latency.

## 2. Related Requirements
- User Story: US-AUTH-010
- Non-Functional Requirements: NFR-4
- Functional Requirements: FR-5

## 3. Preconditions
- User `alice@acme.com` is locked (`locked_until` in the future).
- Email `nonexistent@acme.com` does NOT correspond to any user.
- Email `active@acme.com` is an active, unlocked user (for baseline comparison).
- All users are in tenant "acme."

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Locked user | alice@acme.com | locked_until in future |
| Non-existent user | nonexistent@acme.com | No user record |
| Active user | active@acme.com | For wrong-password baseline |
| Password | AnyP@ssword1 | Irrelevant for locked/non-existent |
| Sample size | 50 requests each | Statistical validity |
| Acceptable variance | P95 within 50ms | Threshold for indistinguishability |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send 50 `POST /api/v1/auth/login` requests for `alice@acme.com` (locked) with any password. Record each response time. | All return HTTP 401. Collect response time distribution. |
| 2 | Send 50 `POST /api/v1/auth/login` requests for `nonexistent@acme.com` with any password. Record each response time. | All return HTTP 401 with "Invalid email or password." Collect response time distribution. |
| 3 | Send 50 `POST /api/v1/auth/login` requests for `active@acme.com` with wrong password. Record each response time. | All return HTTP 401 with "Invalid email or password." Collect response time distribution. |
| 4 | Compute median and P95 response times for each group. | Three distributions calculated. |
| 5 | Compare the locked-account median with the non-existent-account median. | Difference is within 50ms -- response times are similar enough that an attacker cannot distinguish account states. |
| 6 | Compare the locked-account P95 with the active-wrong-password P95. | Difference is within 50ms. The locked path (which skips password hashing) must add deliberate delay or use a consistent code path to mask the timing difference. |
| 7 | Verify the error messages are generic and identical between non-existent and wrong-password scenarios. | Both return `"Invalid email or password."` Locked accounts return the lockout message (acceptable since it is only shown after threshold). |

## 6. Postconditions
- No timing side-channel is exploitable to distinguish locked, non-existent, or active accounts.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
