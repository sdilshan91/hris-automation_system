---
id: TC-AUTH-111
user_story: US-AUTH-010
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-AUTH-111: Lockout at exact boundary -- threshold minus one does not lock, threshold locks

## 1. Test Objective
Verify precise boundary behavior: exactly `maxFailedAttempts - 1` failures do NOT trigger lockout, and exactly `maxFailedAttempts` failures DO trigger lockout. This is a strict boundary test for the threshold logic.

## 2. Related Requirements
- User Story: US-AUTH-010
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme" has lockout policy: `maxFailedAttempts = 5`.
- User `alice@acme.com` has `failed_login_count = 0`, `locked_until = null`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Active user |
| Wrong password | Wr0ngP@ss | Incorrect |
| Max failed attempts | 5 | Threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send 4 consecutive failed login attempts (threshold - 1). | All return HTTP 401 with generic "Invalid email or password." |
| 2 | Query `users.failed_login_count`. | Value is `4`. |
| 3 | Query `users.locked_until`. | Value is `null` -- NOT locked at threshold - 1. |
| 4 | Verify the 4th response message is still the generic error, NOT the lockout message. | No lockout-specific wording. |
| 5 | Send the 5th failed login attempt (exactly at threshold). | HTTP 401 with lockout message: "Account temporarily locked. Try again later or contact your administrator." |
| 6 | Query `users.failed_login_count`. | Value is `5`. |
| 7 | Query `users.locked_until`. | Value is set (in the future) -- locked at exactly the threshold. |
| 8 | Verify there is a clear distinction: attempt 4 did NOT lock, attempt 5 DID lock. | Boundary is precise at `maxFailedAttempts`. |

## 6. Postconditions
- Account locked at exactly the threshold, not before.
- Boundary behavior is deterministic.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
