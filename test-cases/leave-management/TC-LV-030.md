---
id: TC-LV-030
user_story: US-LV-002
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-030: Modify entitlement rule triggers Hangfire recalculation and audit log

## 1. Test Objective
Verify that when an HR Officer modifies an existing entitlement rule and saves, a Hangfire background job is enqueued to recalculate affected employees' balances, and the change is recorded in the audit log with before/after snapshots.

## 2. Related Requirements
- User Story: US-LV-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.
- Entitlement rule exists: "Annual Leave" + Department "Engineering" + Job Level "Senior" = 25 days.
- 3 employees match this rule (Engineering, Senior): Alice, Bob, Carol.
- All three have current leave balances of 25.00 days from previous accrual.
- Hangfire dashboard accessible for job verification.

## 4. Test Data
| Field | Before | After |
|-------|--------|-------|
| Entitlement Days | 25.00 | 28.00 |
| Rule ID | {existing_rule_id} | Same |
| Affected Employees | Alice, Bob, Carol | 3 employees |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the entitlement rule for "Annual Leave" + Engineering + Senior | Rule details load showing 25.00 days. |
| 2 | Edit the rule: change Entitlement Days from 25.00 to 28.00 | Field accepts input. |
| 3 | Click "Save" | API call `PUT /api/v1/leave-entitlement-rules/{rule_id}` returns 200 OK with updated values. |
| 4 | Verify response body shows `entitlement_days: 28.00` | Updated value confirmed. |
| 5 | Verify a Hangfire background job has been enqueued | Check Hangfire dashboard: a job of type "RecalculateEntitlements" (or similar) is queued/processing with the `rule_id` as parameter. |
| 6 | Wait for the Hangfire job to complete | Job status transitions to "Succeeded". |
| 7 | Query Alice's leave balance for "Annual Leave" | Balance updated to 28.00 days (or pro-rated equivalent). |
| 8 | Query Bob's leave balance for "Annual Leave" | Balance updated to 28.00 days (or pro-rated equivalent). |
| 9 | Query Carol's leave balance for "Annual Leave" | Balance updated to 28.00 days (or pro-rated equivalent). |
| 10 | Verify `leave_ledger` adjustment entries for all 3 employees | New ledger entries of type "adjusted" with delta = +3.00 (28 - 25) for each employee. |
| 11 | Query the audit log for this rule_id | Audit entry found with: action type indicating rule update, `rule_id`, `tenant_id`, `user_id`, `timestamp`. |
| 12 | Verify the before-snapshot in the audit record | Contains `{ "entitlement_days": 25.00, ... }`. |
| 13 | Verify the after-snapshot in the audit record | Contains `{ "entitlement_days": 28.00, ... }`. |
| 14 | Verify employees NOT matching the rule are unaffected | An employee in Marketing department still has their original balance. |

## 6. Postconditions
- The entitlement rule record is updated with the new days value.
- A Hangfire job was executed to recalculate affected balances.
- All 3 matching employees have updated balances with adjustment ledger entries.
- An audit log entry with before/after snapshots exists for the rule modification.
- Non-matching employees are unaffected.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
