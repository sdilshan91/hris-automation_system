---
id: TC-CHR-003
user_story: US-CHR-004
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-003: Reject duplicate department name within the same tenant

## 1. Test Objective
Verify that the system rejects creation of a department with a name that already exists within the same tenant, returning the appropriate error message as specified in AC-3.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A department named "Engineering" already exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Existing Department | Engineering | Already exists in tenant |
| New Department Name | Engineering | Duplicate name |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page | Department list page loads; "Engineering" is visible in the list. |
| 2 | Click "Add Department" button | Create form/panel appears. |
| 3 | Enter "Engineering" in the Department Name field | Field accepts the input. |
| 4 | Click "Save" / "Create" button | Request is submitted. |
| 5 | Observe API call `POST /api/v1/departments` with body `{ name: "Engineering" }` | Response status is 409 Conflict (or 422 Unprocessable Entity). |
| 6 | Verify response body contains error message: "A department with this name already exists." | Exact error message matches AC-3 specification. |
| 7 | Verify the error message is displayed in the UI near the Department Name field or as a toast notification | User sees the rejection reason clearly. |
| 8 | Verify no new department record was created in the database | Only one "Engineering" department exists for tenant "acme". |

## 6. Postconditions
- No duplicate department was created.
- The form remains open with user input preserved for correction.
- No audit log entry for a failed creation attempt (or a failed-attempt audit entry per system design).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
