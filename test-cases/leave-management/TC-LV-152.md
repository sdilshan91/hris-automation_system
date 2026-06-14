---
id: TC-LV-152
user_story: US-LV-008
module: Leave Management
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-152: FIFO consumption -- carry-forward days are used before new entitlement (AC-3, BR-4)

## 1. Test Objective
Verify the FIFO principle (BR-4): when an employee takes leave after carry-forward, carried-forward days are consumed first, so the carry-forward balance is reduced before the new-year entitlement is touched. This keeps the carry-forward expiry tracking accurate (AC-3, BR-4).

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-3
- Business Rules: BR-4
- Assumptions/Constraints: Section 10 (FIFO tracked via leave_carry_forward_tracking)

## 3. Preconditions
- Tenant "acme"; employee "Sam" starts the 2027 leave year with 5 carry-forward Annual Leave days + 14 new entitlement = 19 available.
- Carry-forward days expire 2027-03-31.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Carry-forward balance | 5 | consumed first |
| New entitlement | 14 | consumed only after carry-forward |
| Approved leave | 3 days (within Q1 2027) | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Sam takes and gets approved for 3 days of Annual Leave in Q1 2027 | A `used` ledger entry of 3 days is recorded; total available drops from 19 to 16. |
| 2 | Inspect FIFO allocation | The 3 used days are drawn from the carry-forward bucket first: carry-forward remaining = 2; new entitlement = 14 (untouched) (BR-4). |
| 3 | Run `ProcessCarryForwardExpiryJob` after 2027-03-31 | Only the remaining 2 carry-forward days expire (not 5) -- because 3 were already consumed FIFO; an `expired` entry of -2 is created. |
| 4 | Verify new entitlement intact | The 14 new-entitlement days remain fully available; expiry never touches them. |

## 6. Postconditions
- Carry-forward bucket is consumed first (5 -> 2 after 3 used); new entitlement untouched; only the unused carry-forward remainder (2) expires.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
