---
id: TC-CHR-008
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-008: Edit department name and description

## 1. Test Objective
Verify that a Tenant Admin can successfully edit a department's name and description, that the changes are persisted, and that an audit log entry with before/after snapshots is created.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Department "Engineering" exists with description "Core engineering division".
- No department named "Product Engineering" exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Original Name | Engineering | Current name |
| New Name | Product Engineering | Updated name |
| Original Description | Core engineering division | Current description |
| New Description | Product engineering and platform team | Updated description |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page | "Engineering" is visible in the department list. |
| 2 | Click the Edit (pencil icon) action on "Engineering" | Edit form/panel opens with current values pre-populated: Name = "Engineering", Description = "Core engineering division". |
| 3 | Change the Department Name to "Product Engineering" | Field accepts the new value. |
| 4 | Change the Description to "Product engineering and platform team" | Field accepts the new value. |
| 5 | Click "Save" / "Update" button | Loading indicator appears. |
| 6 | Observe API call `PUT /api/v1/departments/{id}` with updated body | Response status is 200 OK. Response body reflects updated name and description. |
| 7 | Verify the department list shows "Product Engineering" with the updated description | Name column shows "Product Engineering". |
| 8 | Verify `updated_at` and `updated_by` are populated in the database | Timestamps reflect the current time; `updated_by` matches the authenticated user. |
| 9 | Verify an audit log entry exists for the update | Audit record contains `action: department_updated`, before/after snapshots showing old and new name/description. |

## 6. Postconditions
- Department name is "Product Engineering" and description is "Product engineering and platform team".
- `updated_at` and `updated_by` fields are set.
- An audit log entry of type `department_updated` has been recorded with change details.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
