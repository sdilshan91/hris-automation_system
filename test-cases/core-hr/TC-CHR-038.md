---
id: TC-CHR-038
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-038: Create job title without a linked grade

## 1. Test Objective
Verify that a job title can be created without linking it to any salary grade, confirming that `grade_id` is nullable and that the system treats grade linking as optional per BR-2.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-3
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- No job title named "Intern Coordinator" exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Title Name | Intern Coordinator | Required |
| Grade | (none) | Explicitly left empty |
| Description | Coordinates internship programs | Optional |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page | Job Titles list page loads. |
| 2 | Click "Add Job Title" | Create form/panel appears. |
| 3 | Enter "Intern Coordinator" in the Title Name field | Field accepts the input. |
| 4 | Explicitly leave the Grade dropdown unselected | No grade is selected; field shows placeholder. |
| 5 | Click "Save" | Request is submitted. |
| 6 | Observe API call `POST /api/v1/job-titles` with body containing no `grade_id` (or `grade_id: null`) | Response status is 201 Created. |
| 7 | Verify response body contains `grade_id: null` | Grade is not set. |
| 8 | Verify the new row in the job titles list | "Intern Coordinator" row shows Grade column as "-" or empty. |

## 6. Postconditions
- A new `job_title` record exists with `grade_id = null`.
- The title is fully functional and can be assigned to employees (when employee management is available).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
