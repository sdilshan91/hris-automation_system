---
id: TC-AUTH-084
user_story: US-AUTH-010
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-084: Lockout triggered at threshold -- locked_until set, lockout message returned, account_locked audit logged

## 1. Test Objective
Verify that when a user reaches the maximum failed attempts threshold (default: 5), the system sets `locked_until` to the correct future timestamp, returns the lockout-specific 401 message, and logs an `account_locked` audit event with all required metadata.

## 2. Related Requirements
- User Story: US-AUTH-010
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-2, FR-3, FR-7
- Data Requirements: `account_locked` audit record schema

## 3. Preconditions
- Tenant "acme" has lockout policy: `maxFailedAttempts = 5`, `lockoutDurationMinutes = 15`.
- User `alice@acme.com` has `failed_login_count = 4` and `locked_until = null` (one attempt away from lockout).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant subdomain | acme | Active tenant |
| User email | alice@acme.com | 4 prior failures |
| Wrong password | Wr0ngP@ss5 | 5th incorrect attempt |
| Max failed attempts | 5 | Tenant policy |
| Lockout duration | 15 minutes | Tenant policy |
| Expected locked_until | now() + 15 minutes | Approximate |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm `alice@acme.com` has `failed_login_count = 4` and `locked_until = null`. | Precondition verified. |
| 2 | Record the current UTC time as `T_before`. | Timestamp captured. |
| 3 | Send `POST /api/v1/auth/login` with wrong password (the 5th consecutive failure). | HTTP 401 returned. |
| 4 | Verify the response body message is `"Account temporarily locked. Try again later or contact your administrator."` | Lockout-specific message is displayed, NOT the generic "Invalid email or password." |
| 5 | Record the current UTC time as `T_after`. | Timestamp captured. |
| 6 | Query `users.failed_login_count` for `alice@acme.com`. | Value is `5`. |
| 7 | Query `users.locked_until` for `alice@acme.com`. | Value is between `T_before + 15 minutes` and `T_after + 15 minutes` (within a reasonable tolerance of a few seconds). |
| 8 | Query the audit log for `account_locked` events for `alice@acme.com`. | Exactly one `account_locked` event exists with: `user_id`, source `IP address`, `attempt_count = 5`, `lockout_until` matching the value from step 7. |
| 9 | Verify the audit event is written to BOTH the tenant audit log and the system audit log. | The event exists in both logs per FR-7. |
| 10 | Verify no JWT or refresh token is present in the response. | No tokens are issued. |

## 6. Postconditions
- `failed_login_count` is 5; `locked_until` is approximately 15 minutes in the future.
- An `account_locked` audit event is recorded in both tenant and system audit logs.
- No tokens were issued.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
