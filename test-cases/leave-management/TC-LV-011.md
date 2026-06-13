---
id: TC-LV-011
user_story: US-LV-001
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-011: Cannot hard-delete a leave type referenced by requests (soft delete only)

## 1. Test Objective
Verify that a leave type referenced by existing leave requests cannot be hard-deleted. The system should only allow deactivation (soft delete). This is a forward-looking test since the leave-request module is not yet built.

## 2. Related Requirements
- User Story: US-LV-001
- Functional Requirements: FR-1, FR-5
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" has an active leave type "Sick Leave".
- At least one leave request references "Sick Leave" (FORWARD-LOOKING: if leave-request module not built, verify that the API endpoint for delete either does not exist or returns the correct error for any referenced type).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Leave Type | Sick Leave | Has existing references (forward-looking) |
| Leave Request | John Doe, 2 days Sick Leave, Approved | Reference preventing hard delete |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Attempt `DELETE /api/v1/leave-types/{sick_leave_id}` | API returns 409 Conflict or 400 Bad Request: "This leave type cannot be deleted because it is referenced by existing leave requests. You can deactivate it instead." Mark step DEFERRED if leave-request FK not yet implemented; verify the API design prevents hard delete by returning appropriate error or 405 Method Not Allowed. |
| 2 | Verify the leave type still exists in the database | `SELECT * FROM leave_type WHERE leave_type_id = {id}` returns the record with `is_deleted = false`. |
| 3 | Verify the UI does not expose a "Delete" button for leave types with references (FORWARD-LOOKING) | Only "Deactivate" option is available. If reference tracking not built, verify no hard-delete button exists at all (only deactivate). |
| 4 | Deactivate the leave type instead | `PATCH /api/v1/leave-types/{id}` with `{ is_active: false }` succeeds. Response 200 OK. |
| 5 | Verify deactivated type is retained for historical reporting | Record has `is_active = false`, `is_deleted = false`. Historical queries still include it. |

## 6. Postconditions
- Leave type is NOT hard-deleted from the database.
- Leave type can be deactivated (soft delete) successfully.
- All historical references remain intact.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
