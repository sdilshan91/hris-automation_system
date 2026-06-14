---
id: TC-LV-192
user_story: US-LV-010
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-192: Cancelling an approved leave that has ALREADY STARTED is blocked with the contact-HR message (negative; AC-3, BR-3)

## 1. Test Objective
Verify that an attempt to cancel an approved leave whose start date is in the past (the leave is in progress) is rejected with the exact message "Cannot cancel leave that has already started. Please contact HR for assistance.", no status change occurs, and no reversal ledger entry is written (AC-3, BR-3).

## 2. Related Requirements
- User Story: US-LV-010
- Acceptance Criteria: AC-3
- Business Rules: BR-3
- Functional Requirements: FR-7 (default policy: cancellation only allowed before start)

## 3. Preconditions
- Tenant "acme"; today is 2026-06-14.
- Employee "Jane Smith" has an APPROVED Annual Leave request R: 2026-06-12..06-16 (started 2 days ago, still ongoing), `total_days = 5`, status `Approved`, with a `used -5.00` ledger entry.
- Tenant cancellation policy is the default (no partial/after-start cancellation; FR-7 N=0).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Today | 2026-06-14 | within the leave window |
| Request R | Annual Leave, 2026-06-12..06-16, 5 days | Approved, in progress |
| Reason | "Returned early" | provided but irrelevant -- still blocked |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jane, attempt `POST /api/v1/leaves/{R}/cancel` with a reason | Request is rejected (HTTP 400 / 422) with the message: "Cannot cancel leave that has already started. Please contact HR for assistance." |
| 2 | Inspect R's status | Unchanged -- `status = Approved`; no `cancelled_at`. |
| 3 | Query `leave_ledger` | No reversal `adjusted` row was written; the original `used -5.00` row is intact. |
| 4 | (UI) Verify the cancel affordance | The Cancel button is disabled/hidden for R with a tooltip explaining why (past/in-progress), per Section 8. |
| 5 | Re-read Jane's balance | Unchanged (still reflects the -5 deduction). |

## 6. Postconditions
- The in-progress approved leave remains Approved; balance unchanged; the contact-HR message is surfaced.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
