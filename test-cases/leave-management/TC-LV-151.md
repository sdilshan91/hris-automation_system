---
id: TC-LV-151
user_story: US-LV-008
module: Leave Management
priority: critical
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-151: Monthly expiry job expires carry-forward days past their 3-month expiry (AC-3, BR-3, FR-3, FR-6; Redis invalidation DEFERRED)

## 1. Test Objective
Verify the monthly `ProcessCarryForwardExpiryJob` creates an `expired` ledger entry for carried-forward days that remain unused past their `carry_forward_expiry_months` expiry date (AC-3, BR-3, FR-3, FR-6). The Redis cache invalidation step (FR-7, AC-3) is DEFERRED module-wide; the DB/ledger effect is verified live and the cache step is recorded as conditional.

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3, FR-6, FR-7 (cache invalidation DEFERRED)
- Business Rules: BR-3
- Note: Redis cache invalidation DEFERRED per docs/vault/modules/leave-management.md (no cache layer; balance read from LeaveLedger running total).

## 3. Preconditions
- Tenant "acme"; Annual Leave `carry_forward_expiry_months = 3`.
- Employee "Sam" has 5 carry-forward Annual Leave days from a prior year-end, with `transaction_date` = 2027-01-01, so expiry date = 2027-03-31 (3 months from the first day of the new leave year, BR-3).
- The 5 carry-forward days are still unused as of the expiry boundary.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Carry-forward days | 5 | from 2026 year-end |
| Carry-forward date | 2027-01-01 | first day of new leave year |
| Expiry date | 2027-03-31 | +3 months (BR-3) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run `ProcessCarryForwardExpiryJob` on a date BEFORE 2027-03-31 (e.g. 2027-03-01) | No `expired` entry is created for the carry-forward days -- they have not yet expired (BR-3 boundary). |
| 2 | Advance the clock past expiry and run the monthly job (e.g. 2027-04-01) | One `expired` ledger entry of -5 days is created for the unexpired carry-forward remainder (AC-3, FR-3, FR-6). |
| 3 | Read Sam's Annual Leave balance | The 5 carry-forward days no longer count toward the available balance. |
| 4 | Record cache behavior | Redis cache invalidation (FR-7) is DEFERRED -- balance is recomputed from the ledger on read; the cache-invalidation step is CONDITIONAL pending the Redis layer (not a silent gap). |

## 6. Postconditions
- Unused carry-forward days past expiry are forfeited via an `expired` ledger entry; balance reflects the loss; cache-invalidation deferred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
