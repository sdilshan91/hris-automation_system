---
id: TC-AUTH-101
user_story: US-AUTH-010
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-101: Audit trail -- all lockout-related event types are recorded with correct metadata

## 1. Test Objective
Verify that the full lifecycle of lockout events generates the correct audit records: `login_failure` (with attempt count), `account_locked`, `account_unlocked_by_admin`, and `account_unlocked_by_timeout`. Each event must contain all required metadata as specified in the Data Requirements.

## 2. Related Requirements
- User Story: US-AUTH-010
- Functional Requirements: FR-7
- Data Requirements: Audit record schemas for all four event types

## 3. Preconditions
- Tenant "acme" has lockout policy: `maxFailedAttempts = 5`, `lockoutDurationMinutes = 15`.
- User `alice@acme.com` has `failed_login_count = 0`, `locked_until = null`.
- Tenant admin `admin@acme.com` is available for the manual unlock step.
- Audit log is accessible for querying.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User email | alice@acme.com | Test subject |
| Tenant admin | admin@acme.com | For manual unlock |
| Client IP | 192.168.1.100 | Source IP for audit |
| Max failed attempts | 5 | Threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **login_failure events:** Send 5 failed login attempts from IP `192.168.1.100`. | 5 `login_failure` audit events created. |
| 2 | Query the audit log for `login_failure` events for `alice@acme.com`. | 5 events exist, each with: `user_id`, `ip_address` (192.168.1.100), `attempt_count` (1 through 5), `timestamp`, `event_type = login_failure`. |
| 3 | **account_locked event:** Verify the 5th failure also creates an `account_locked` event. | `account_locked` event with: `user_id`, `ip_address`, `attempt_count = 5`, `lockout_until` timestamp, `event_type = account_locked`. |
| 4 | Verify both the tenant audit log and system audit log contain the `account_locked` event (FR-7). | Event exists in both logs. |
| 5 | **account_unlocked_by_admin event:** As `admin@acme.com`, unlock `alice@acme.com`. | `account_unlocked_by_admin` event with: `user_id` (alice), `admin_user_id` (admin), `timestamp`, `event_type = account_unlocked_by_admin`. |
| 6 | Re-lock alice by failing 5 more times. Wait for lockout to expire. Log in successfully. | Lockout expires; login succeeds. |
| 7 | **account_unlocked_by_timeout event:** Query the audit log. | `account_unlocked_by_timeout` event with: `user_id` (alice), `timestamp`, `event_type = account_unlocked_by_timeout`. |
| 8 | Verify all audit events across the full lifecycle have sequential, consistent timestamps. | Events are in chronological order: login_failure(x5) -> account_locked -> account_unlocked_by_admin -> login_failure(x5) -> account_locked -> account_unlocked_by_timeout. |

## 6. Postconditions
- Complete audit trail of all four event types is verified.
- Each event contains all required metadata fields.
- Events are recorded in both tenant and system audit logs.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
