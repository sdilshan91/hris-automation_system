---
id: TC-LV-097
user_story: US-LV-005
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-097: Multi-level approval -- first-level approval moves the request to "Pending L2 Approval" and notifies the next approver (CONDITIONAL on approval-workflow config)

## 1. Test Objective
Verify that when a tenant has a multi-level (2-level) approval workflow configured and the acting manager is not the final approver, approving the request transitions it to "Pending L2 Approval" (next level) rather than "Approved", records the level-1 decision in `leave_approval_history`, and notifies the next-level approver. NO `used` ledger entry is created until the final-level approval (AC-4, FR-5).

## 2. Related Requirements
- User Story: US-LV-005
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5
- Dependencies: approval-workflow configuration (US-ADM-007) -- CONDITIONAL/forward-looking

## 3. Preconditions
- Tenant "acme" is active.
- A 2-level approval workflow is configured for "acme": level-1 = direct manager (Robert Lee), level-2 = department head (Sara Voss).
- Direct report "Jane Smith" has a pending request requiring level-1 then level-2 approval.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Workflow | 2 levels | L1 = Robert, L2 = Sara |
| Request | Jane Smith, Annual Leave, 3 days | Status Pending (L1) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert (L1), approve Jane's request | API returns 200; `status = Pending L2 Approval` (next level), NOT `Approved`. |
| 2 | Query `leave_approval_history` | A row exists with `approval_level = 1`, `action = Approved`, `approver_employee_id = Robert`. |
| 3 | Query `leave_ledger` | NO `used` entry is created yet (balance deducts only at final-level approval, BR-5/FR-3). |
| 4 | Inspect the notification seam | A notification is queued to the level-2 approver (Sara) that the request awaits her decision. |
| 5 | As Sara (L2), approve | `status = Approved`; a `used` ledger entry is now created; level-2 history row recorded; `leave-approved` notification queued to Jane. |
| 6 | If the approval-workflow configuration (US-ADM-007) is NOT yet implemented | This test is marked CONDITIONAL/DEFERRED on that story: single-level approval (TC-LV-089) is the verified default now, and the multi-level transition is validated when the workflow config exists. This is NOT a silent pass -- the dependency is recorded explicitly. |

## 6. Postconditions
- With multi-level config: request moves L1 -> Pending L2 -> Approved across two decisions; ledger entry created only at final approval.
- Without config: default single-level path applies (TC-LV-089).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
