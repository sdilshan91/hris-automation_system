---
id: TC-CHR-045
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-045: Edit job title name to an existing name is rejected

## 1. Test Objective
Verify that when editing a job title, changing its name to one that already exists within the same tenant is rejected with the appropriate duplicate name error, enforcing the uniqueness constraint on updates as well as creates.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-3
- Functional Requirements: FR-1, FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Two job titles exist in the "acme" tenant: "Software Engineer" and "QA Engineer".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Title Being Edited | QA Engineer | Existing title |
| New Name | Software Engineer | Conflicts with existing title |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page | Both "Software Engineer" and "QA Engineer" are visible. |
| 2 | Click "Edit" on the "QA Engineer" row | Edit form opens with Title Name pre-populated as "QA Engineer". |
| 3 | Change the Title Name to "Software Engineer" | Field accepts the input. |
| 4 | Click "Save" | Request is submitted. |
| 5 | Observe API call `PUT /api/v1/job-titles/{qa_engineer_id}` with body `{ title_name: "Software Engineer" }` | Response status is 409 Conflict (or 422 Unprocessable Entity). |
| 6 | Verify response body contains error message: "A job title with this name already exists." | Error message matches the duplicate name rejection per AC-3. |
| 7 | Verify the error is displayed in the UI | User sees the rejection reason. |
| 8 | Verify the "QA Engineer" title is unchanged in the database | Name is still "QA Engineer". |

## 6. Postconditions
- The "QA Engineer" job title name is not changed.
- The form remains open with user input for correction.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
