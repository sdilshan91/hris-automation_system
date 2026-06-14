---
id: TC-LV-210
user_story: US-LV-011
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-210: Zero-balance leave application is offered as LOP and, on confirm, creates a request with leave_type=LOP and is_lop=true (happy path)

## 1. Test Objective
Verify that when an employee applies for a leave type for which they have zero balance (and the type does NOT allow negative balance), the system surfaces the prompt "Insufficient balance. This will be processed as Loss of Pay (LOP)." and, when the employee confirms, persists a `leave_request` with the LOP leave type, `is_lop = true`, and `lop_source = employee_request` (AC-1, FR-3/FR-4, BR-1).

## 2. Related Requirements
- User Story: US-LV-011
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-4
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme"; the system LOP/"Unpaid Leave" leave type exists (FR-1).
- Employee "Jane Smith" has 0 remaining days for the selected (non-LOP) leave type, which has `negative_balance_allowed = false`.
- Jane is authenticated with `Leave.Apply`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Selected type | Annual Leave | balance = 0, no negative allowed |
| Requested dates | 2 working days | future, no overlap/holiday |
| Confirmation | Yes | confirms LOP |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Jane submits a leave request for 2 working days of Annual Leave (balance 0) | The system does NOT silently reject; it returns the prompt "Insufficient balance. This will be processed as Loss of Pay (LOP)." (insufficient-balance soft signal, not a hard block). |
| 2 | Jane confirms "process as LOP" | A `leave_request` is created with `leave_type = LOP`, `is_lop = true`, `lop_source = employee_request`, `total_days = 2`, tenant-stamped to acme. |
| 3 | Inspect the created request | Status reflects the request lifecycle (Pending/awaiting approval per tenant policy); the request is linked to Jane and the LOP type; the original zero-balance Annual type is NOT decremented (LOP has no balance — BR-1). |
| 4 | Re-read Jane's Annual Leave balance | Unchanged at 0 (the LOP path did not touch the Annual ledger). |

## 6. Postconditions
- An LOP-flagged leave request exists for Jane; no balance was deducted from the zero-balance type.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
