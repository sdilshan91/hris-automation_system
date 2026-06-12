---
id: TC-CHR-040
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-040: Deactivate job title with no assigned employees (success)

## 1. Test Objective
Verify that a Tenant Admin can successfully deactivate a job title that has no active employees assigned to it, and that the title's status changes to inactive, an audit log entry is created, and the title is hidden from assignment dropdowns but remains visible in the admin view.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-5, FR-7
- Non-Functional Requirements: NFR-4
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A job title "Temp Analyst" exists in the "acme" tenant with `is_active = true`.
- No employees are currently assigned to "Temp Analyst".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Job Title | Temp Analyst | Active, zero assigned employees |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page | Job Titles list page loads; "Temp Analyst" is visible with Status: Active and Employee Count: 0. |
| 2 | Click the "Deactivate" action (or toggle inline status) on the "Temp Analyst" row | A confirmation dialog appears asking the user to confirm deactivation. |
| 3 | Confirm the deactivation | Request is submitted. |
| 4 | Observe API call `PATCH /api/v1/job-titles/{job_title_id}/deactivate` (or `PUT` with `is_active: false`) | Response status is 200 OK. Response body shows `is_active: false`. |
| 5 | Verify the "Temp Analyst" row in the admin list now shows Status: "Inactive" | Status indicator changes to inactive (visually distinct). |
| 6 | Verify `is_deleted` remains `false` (soft delete, not hard delete) | The record is not physically removed from the database. |
| 7 | Verify an audit log entry exists for the deactivation | Audit record contains `action: job_title_deactivated`, `entity_id`, `tenant_id`, `user_id`, and timestamp. |

## 6. Postconditions
- The job title record has `is_active = false` and `is_deleted = false`.
- The title remains visible in the admin Job Titles list (with inactive status).
- The title is hidden from employee assignment dropdowns (verified in TC-CHR-048).
- An audit log entry of type `job_title_deactivated` has been recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
