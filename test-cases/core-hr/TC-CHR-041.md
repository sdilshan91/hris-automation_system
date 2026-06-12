---
id: TC-CHR-041
user_story: US-CHR-005
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-041: Reject duplicate job title name within the same tenant

## 1. Test Objective
Verify that the system rejects creation of a job title with a name that already exists within the same tenant, returning the error message specified in AC-3: "A job title with this name already exists."

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A job title named "Software Engineer" already exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Existing Title | Software Engineer | Already exists in tenant |
| New Title Name | Software Engineer | Duplicate name |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page | Job Titles list page loads; "Software Engineer" is visible. |
| 2 | Click "Add Job Title" | Create form/panel appears. |
| 3 | Enter "Software Engineer" in the Title Name field | Field accepts the input. |
| 4 | Click "Save" | Request is submitted. |
| 5 | Observe API call `POST /api/v1/job-titles` with body `{ title_name: "Software Engineer" }` | Response status is 409 Conflict (or 422 Unprocessable Entity). |
| 6 | Verify response body contains error message: "A job title with this name already exists." | Exact error message matches AC-3 specification. |
| 7 | Verify the error message is displayed in the UI near the Title Name field or as a toast notification | User sees the rejection reason clearly. |
| 8 | Verify no new job title record was created in the database | Only one "Software Engineer" title exists for tenant "acme". |

## 6. Postconditions
- No duplicate job title was created.
- The form remains open with user input preserved for correction.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
