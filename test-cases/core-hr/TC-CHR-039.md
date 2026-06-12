---
id: TC-CHR-039
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-039: Edit job title name, description, and grade link

## 1. Test Objective
Verify that a Tenant Admin can edit an existing job title's name, description, and grade association, and that the changes are persisted, reflected in the list, and an audit log entry is created.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-3
- Non-Functional Requirements: NFR-4
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A job title "Junior Developer" exists in the "acme" tenant with no grade linked.
- A salary grade "L3 - Junior" exists in the "acme" tenant.
- No job title named "Associate Developer" exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Existing Title | Junior Developer | To be edited |
| New Title Name | Associate Developer | Updated name |
| New Grade | L3 - Junior | Newly linked grade |
| New Description | Entry-level development role | Updated description |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page | Job Titles list page loads; "Junior Developer" is visible. |
| 2 | Click the "Edit" action on the "Junior Developer" row | Edit form/panel opens with current values pre-populated: Title Name = "Junior Developer", Grade = empty, Description = current value. |
| 3 | Change the Title Name to "Associate Developer" | Field accepts the new value. |
| 4 | Select "L3 - Junior" from the Grade dropdown | Grade is selected. |
| 5 | Change the Description to "Entry-level development role" | Field accepts the new value. |
| 6 | Click "Save" | Request is submitted. |
| 7 | Observe API call `PUT /api/v1/job-titles/{job_title_id}` with updated fields | Response status is 200 OK. Response body reflects the updated name, grade_id, and description. |
| 8 | Verify the list now shows "Associate Developer" instead of "Junior Developer" | Row updated with new name, Grade: "L3 - Junior". |
| 9 | Verify `updated_at` and `updated_by` are populated on the record | Timestamps and user ID are set. |
| 10 | Verify an audit log entry exists for the update operation | Audit record contains `action: job_title_updated`, changed fields, `entity_id`, `tenant_id`, `user_id`, and timestamp. |

## 6. Postconditions
- The job title record has been updated with the new name, grade_id, and description.
- `updated_at` and `updated_by` are set.
- An audit log entry of type `job_title_updated` has been recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
