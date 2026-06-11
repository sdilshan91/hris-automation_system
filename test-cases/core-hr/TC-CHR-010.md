---
id: TC-CHR-010
user_story: US-CHR-004
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-010: Deactivate department blocked when active employees are assigned

## 1. Test Objective
Verify that the system blocks deactivation of a department that has active employees assigned, displaying the warning message specified in AC-5: "This department has X active employees. Please reassign them before deactivating."

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Department "Engineering" exists with `is_active = true`.
- At least 3 active employees are assigned to "Engineering" (requires US-CHR-001 employee data; seed test data if available).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department | Engineering | Has 3 active employees |
| Employee Count | 3 | Active employees assigned |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page | "Engineering" is visible with Employee Count = 3. |
| 2 | Click the Deactivate (archive icon) action on "Engineering" | A confirmation dialog appears. |
| 3 | Confirm the deactivation in the dialog | API call `PUT /api/v1/departments/{id}/deactivate` (or `PATCH`) is made. |
| 4 | Verify API returns an error (409 Conflict or 422 Unprocessable Entity) | Response body contains: "This department has 3 active employees. Please reassign them before deactivating." |
| 5 | Verify the warning message is displayed in the UI | Dialog or toast shows the employee count and reassignment instruction. |
| 6 | Verify "Engineering" remains active in the department list | Status column still shows "Active". `is_active` remains `true` in the database. |
| 7 | Verify no audit log entry for deactivation was created | The operation was rejected; no state change occurred. |

## 6. Postconditions
- "Engineering" remains active with `is_active = true`.
- Employee assignments are unchanged.
- No deactivation audit entry was created.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
