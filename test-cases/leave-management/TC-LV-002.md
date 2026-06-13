---
id: TC-LV-002
user_story: US-LV-001
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-002: Edit leave type entitlement and carry-forward with audit trail

## 1. Test Objective
Verify that an HR Officer can edit an existing leave type's entitlement and carry-forward settings, that changes are saved with a full audit trail capturing before/after JSON snapshots, and that changes take effect for the next accrual cycle (not retroactively).

## 2. Related Requirements
- User Story: US-LV-001
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-2
- Non-Functional Requirements: NFR-3
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with an active leave type "Annual Leave" with annual_entitlement = 20.00, carry_forward_limit = 5.00, carry_forward_expiry_months = 3.
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Existing Leave Type | Annual Leave | leave_type_id known |
| Original Entitlement | 20.00 | Before edit |
| New Entitlement | 25.00 | After edit |
| Original Carry Forward Limit | 5.00 | Before edit |
| New Carry Forward Limit | 8.00 | After edit |
| Original Carry Forward Expiry | 3 | months |
| New Carry Forward Expiry | 6 | months |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Leave Types configuration page and click "Edit" on "Annual Leave" | Slide-over panel opens pre-populated with existing values: entitlement = 20, carry_forward_limit = 5, carry_forward_expiry = 3. |
| 2 | Change Annual Entitlement from 20.00 to 25.00 | Field updates to 25.00; no validation error. |
| 3 | Change Carry Forward Limit from 5.00 to 8.00 | Field updates to 8.00. |
| 4 | Change Carry Forward Expiry from 3 to 6 months | Field updates to 6. |
| 5 | Click "Save" button | Loading indicator appears; button is disabled. |
| 6 | Observe API call `PUT /api/v1/leave-types/{leave_type_id}` | Request sent. Response status is 200 OK with updated values. |
| 7 | Verify response body contains updated values: annual_entitlement = 25.00, carry_forward_limit = 8.00, carry_forward_expiry_months = 6 | All changed fields reflect new values. |
| 8 | Verify `updated_at` and `updated_by` are populated with current timestamp and user | Audit columns correctly updated. |
| 9 | Query audit log for this leave_type_id | Audit record contains `action: leave_type_updated`, before-snapshot `{ annual_entitlement: 20.00, carry_forward_limit: 5.00, carry_forward_expiry_months: 3 }` and after-snapshot `{ annual_entitlement: 25.00, carry_forward_limit: 8.00, carry_forward_expiry_months: 6 }`. |
| 10 | Verify that the change is noted as effective for the next accrual cycle | API response or audit metadata indicates changes apply prospectively, not retroactively to already-accrued balances. |

## 6. Postconditions
- The `leave_type` record is updated with new entitlement and carry-forward values.
- `updated_at` and `updated_by` reflect the editing user and timestamp.
- An audit log entry with before/after JSON snapshots exists.
- Already-approved leave requests remain unaffected (BR-5).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
