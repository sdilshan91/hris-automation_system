---
id: TC-CHR-037
user_story: US-CHR-005
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-037: Create job title with salary grade link

## 1. Test Objective
Verify that a Tenant Admin can create a new job title and link it to an existing salary grade via the `grade_id` FK. When saved, the grade association is persisted and displayed on the job titles list. This validates AC-4 (the grade-link portion; the employee-profile display is deferred to US-CHR-001).

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1, FR-3
- Business Rules: BR-2, BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A salary grade "L5 - Senior" exists in the "acme" tenant.
- No job title named "Senior Developer" exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Title Name | Senior Developer | Required |
| Grade | L5 - Senior | Existing grade in same tenant |
| Description | Senior-level software developer | Optional |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page | Job Titles list page loads. |
| 2 | Click "Add Job Title" | Create form/panel appears with Grade dropdown. |
| 3 | Enter "Senior Developer" in the Title Name field | Field accepts the input. |
| 4 | Open the Grade dropdown and search for "L5" | "L5 - Senior" appears in the searchable dropdown results. |
| 5 | Select "L5 - Senior" from the dropdown | Grade is selected; field shows "L5 - Senior". |
| 6 | Enter "Senior-level software developer" in the Description field | Field accepts the input. |
| 7 | Click "Save" | Request is submitted. |
| 8 | Observe API call `POST /api/v1/job-titles` with body including `grade_id` matching the UUID of "L5 - Senior" | Response status is 201 Created. Response body contains `grade_id` set to the UUID of the selected grade. |
| 9 | Verify the new row in the job titles list | "Senior Developer" row shows Grade column value as "L5 - Senior". |
| 10 | Call `GET /api/v1/job-titles/{job_title_id}` and verify the `grade_id` FK is correctly set | Response contains `grade_id` matching the "L5 - Senior" grade UUID. |

## 6. Postconditions
- A new `job_title` record exists with `grade_id` set to the selected grade's UUID.
- The grade relationship is persisted and queryable.
- Note: Verification that this grade appears on employee profiles when the title is assigned is deferred to US-CHR-001 (Employee entity not yet built).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
