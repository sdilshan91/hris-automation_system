---
id: TC-LV-037
user_story: US-LV-002
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-037: Bulk entitlement assignment UI for mass updates

## 1. Test Objective
Verify that the bulk entitlement assignment UI allows an HR Officer to apply or update entitlement rules for multiple employees or groups at once, and that all affected employees have their balances recalculated via a Hangfire background job.

## 2. Related Requirements
- User Story: US-LV-002
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.
- Leave type "Annual Leave" exists and is active.
- At least 10 employees exist across multiple departments.
- No per-employee overrides exist for these employees.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Leave Type | Annual Leave | Target leave type |
| Target Group | All Engineering employees (8 employees) | Bulk target |
| New Entitlement | 22.00 days | New rule value |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Leave Entitlement configuration page | Page loads with the entitlement matrix. |
| 2 | Select "Bulk Update" or "Mass Assignment" action | Bulk assignment form/dialog appears. |
| 3 | Select Leave Type = "Annual Leave" | Leave type selected. |
| 4 | Select target: Department = "Engineering" (or select all Engineering employees) | 8 employees shown in preview/confirmation list. |
| 5 | Set Entitlement Days = 22.00 | Value accepted. |
| 6 | Click "Apply to All" or "Save" | Confirmation dialog shows: "This will update entitlement for 8 employees. Proceed?" |
| 7 | Confirm the bulk update | API returns 200 OK. A Hangfire job is enqueued for recalculation. |
| 8 | Wait for the Hangfire job to complete | Job completes successfully. |
| 9 | Verify all 8 Engineering employees now have 22.00 days Annual Leave entitlement | All 8 balances updated. |
| 10 | Verify employees in other departments are unaffected | Non-Engineering employees retain their original balances. |
| 11 | Verify audit log entries for the bulk update | Audit entry indicates bulk operation with count of affected employees. |

## 6. Postconditions
- All 8 Engineering employees have updated leave entitlements of 22.00 days.
- `leave_ledger` entries reflect the adjustments for each employee.
- Non-targeted employees are unaffected.
- Audit trail records the bulk operation.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
