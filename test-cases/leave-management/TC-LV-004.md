---
id: TC-LV-004
user_story: US-LV-001
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-004: Deactivate leave type -- hidden from apply dropdown, existing requests unaffected

## 1. Test Objective
Verify that deactivating a leave type hides it from the employee leave application dropdown, but existing approved leave requests for that type remain unaffected and visible in historical reports.

## 2. Related Requirements
- User Story: US-LV-001
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1, FR-5
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" has an active leave type "Casual Leave" with `is_active = true`.
- At least one employee in "acme" has an approved leave request of type "Casual Leave" (forward-looking: depends on leave-request module; if unavailable, verify deactivation behavior and dropdown exclusion only).
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Leave Type | Casual Leave | Currently active |
| Approved Request | Employee "John Doe", 3 days, status: Approved | Pre-existing (forward-looking) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Leave Types configuration page | "Casual Leave" is listed with status "Active" and an inline toggle switch. |
| 2 | Toggle the status of "Casual Leave" to Inactive (or click Deactivate action) | Confirmation dialog appears: "Deactivating this leave type will prevent new leave applications. Existing approved requests will not be affected. Continue?" |
| 3 | Confirm deactivation | API call `PATCH /api/v1/leave-types/{id}` with `{ is_active: false }`. Response 200 OK. |
| 4 | Verify "Casual Leave" now shows status "Inactive" in the leave types list | Status badge changes to "Inactive" (grayed out). |
| 5 | Navigate to the employee leave application form (as employee or simulated) | The leave type dropdown does NOT include "Casual Leave". Only active leave types appear. |
| 6 | Verify API `GET /api/v1/leave-types?active=true` does not include "Casual Leave" | Response list contains only active leave types. |
| 7 | Verify existing approved leave request for "Casual Leave" remains intact (FORWARD-LOOKING) | If leave-request module exists: query approved requests, confirm "Casual Leave" request is still present with status "Approved". If module not built, mark this step DEFERRED. |
| 8 | Verify "Casual Leave" still appears in historical leave reports (FORWARD-LOOKING) | Leave history/report endpoints still include past "Casual Leave" records. Mark DEFERRED if reports module not built. |
| 9 | Verify audit log entry for deactivation | Audit record with `action: leave_type_deactivated`, before `{ is_active: true }`, after `{ is_active: false }`. |

## 6. Postconditions
- `leave_type` record has `is_active = false`, `is_deleted = false` (soft deactivation, not deletion).
- Deactivated type excluded from employee-facing leave application dropdowns.
- Historical data and approved requests remain unaffected.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
