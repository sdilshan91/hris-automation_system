---
id: TC-LV-114
user_story: US-LV-006
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-114: Submitting a leave increases "pending" but does not decrease "balance" until approval

## 1. Test Objective
Verify the pending-separation rule: when an employee submits a new leave request, the dashboard's "pending" count for that leave type increases, while "balance" remains unchanged; on approval, balance decreases and pending clears (BR-2, FR-2).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated.
- Annual Leave before action: entitlement 14, carryForward 0, used 4, pending 0, balance 10.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| New request | Annual, 2 days | Future dates, no overlap |
| Balance before | 10 | -- |
| Pending before | 0 | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Read the Annual card baseline | balance=10, pending=0. |
| 2 | Submit a 2-day Annual leave request (US-LV-003 flow), then reload the dashboard | Annual card now shows pending=2 and balance still=10 (balance NOT reduced by pending). |
| 3 | Manager approves the request (US-LV-005), then reload the dashboard | pending returns to 0 and balance decreases to 8 (a 'Used' ledger entry was written at approval). |
| 4 | Open the Annual ledger | A 'Used' entry of 2 days appears dated at approval time; no 'Used' entry existed while the request was merely pending. |

## 6. Postconditions
- Pending is tracked separately from balance; balance changes only on approval.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
