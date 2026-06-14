---
id: TC-LV-193
user_story: US-LV-010
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-193: Start-date boundary -- a leave starting today is treated as already started (blocked); a leave starting tomorrow is cancellable (boundary; AC-3, BR-3)

## 1. Test Objective
Verify the exact boundary of the "already started" rule: an approved leave whose start date equals today is blocked (the leave has started), while an approved leave whose start date is the next day (and no part has elapsed) is still cancellable. Also verify a fully-past approved leave is blocked (AC-3, BR-3, FR-7 default N=0).

## 2. Related Requirements
- User Story: US-LV-010
- Acceptance Criteria: AC-3
- Business Rules: BR-3
- Functional Requirements: FR-7

## 3. Preconditions
- Tenant "acme"; today is 2026-06-14.
- Approved requests for "Jane Smith":
  - R-today: starts 2026-06-14 (today)
  - R-tomorrow: starts 2026-06-15 (future)
  - R-past: 2026-05-20..05-22 (fully elapsed)

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| R-today start | 2026-06-14 | == today -> blocked |
| R-tomorrow start | 2026-06-15 | future -> cancellable |
| R-past | 2026-05-20..05-22 | fully past -> blocked |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Attempt to cancel R-today | Blocked with "Cannot cancel leave that has already started..." (start date == today counts as started). |
| 2 | Cancel R-tomorrow with a reason | Succeeds -- status Cancelled, reversal `adjusted` ledger entry written (it has not started). |
| 3 | Attempt to cancel R-past | Blocked with the same already-started/HR message (the leave is fully in the past). |
| 4 | Verify ledger | A reversal entry exists only for R-tomorrow; R-today and R-past have no reversal. |

## 6. Postconditions
- The boundary is enforced at start-date == today; only the strictly-future request is cancellable.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
