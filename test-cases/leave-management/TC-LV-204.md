---
id: TC-LV-204
user_story: US-LV-010
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-204: Audit log captures before/after state of the cancelled leave request (NFR-4, FR-6)

## 1. Test Objective
Verify that every cancellation (pending and approved) writes an audit record capturing the before/after state of the leave request -- including the status transition, `cancelled_at`, `cancellation_reason`, and the acting employee -- and that the cancellation is recorded in `leave_approval_history` with `action = Cancelled` and the employee as actor (NFR-4, FR-6).

## 2. Related Requirements
- User Story: US-LV-010
- Non-Functional Requirements: NFR-4
- Functional Requirements: FR-6
- Acceptance Criteria: AC-1, AC-2

## 3. Preconditions
- Tenant "acme".
- Employee "Jane Smith" has a PENDING request R-p and an APPROVED future request R-a.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| R-p | Pending | cancel -> Cancelled |
| R-a | Approved future | cancel -> Cancelled (+ reversal) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Cancel R-p (pending) | Audit record written: before `status=Pending`, after `status=Cancelled`; `cancelled_at` and actor=Jane captured; no ledger delta noted. |
| 2 | Cancel R-a (approved, with reason) | Audit record written: before `status=Approved`, after `status=Cancelled`; `cancellation_reason` captured; the reversal `adjusted` ledger delta is reflected in the after-state balance. |
| 3 | Inspect `leave_approval_history` for both | Each has `action = Cancelled`, `approver_employee_id = Jane` (self, FR-6). |
| 4 | Verify the audit actor and timestamp | Audit records show Jane as the actor (resolved from `ICurrentUser`), with a UTC timestamp; tenant_id is stamped. |
| 5 | Verify immutability | The before/after snapshots are persisted (not overwritten by later reads); the original `used` ledger row for R-a is retained alongside the reversal. |

## 6. Postconditions
- Both cancellations are fully audited with before/after state and recorded in approval history with the employee as actor.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
