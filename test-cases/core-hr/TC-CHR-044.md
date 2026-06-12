---
id: TC-CHR-044
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-044: Create job title with empty required fields fails validation

## 1. Test Objective
Verify that the system rejects creation of a job title when the required field `title_name` is missing or empty, returning an appropriate validation error.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Title Name | (empty) | Required field left blank |
| Title Name (whitespace) | "   " | Only whitespace characters |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page and click "Add Job Title" | Create form/panel appears. |
| 2 | Leave the Title Name field empty and click "Save" | Client-side validation prevents submission; "Title Name is required" error is displayed. |
| 3 | Enter only whitespace ("   ") in the Title Name field and click "Save" | Client-side validation prevents submission (or server rejects); error message indicates title name is required. |
| 4 | If client-side validation is bypassed, call `POST /api/v1/job-titles` with `{ title_name: "" }` directly | Response status is 400 Bad Request with validation error: "Title Name is required." |
| 5 | Call `POST /api/v1/job-titles` with `{ title_name: "   " }` (whitespace only) directly | Response status is 400 Bad Request with validation error. |
| 6 | Call `POST /api/v1/job-titles` with no `title_name` field at all | Response status is 400 Bad Request with validation error. |
| 7 | Verify no job title record was created in the database for any of the above attempts | No records with empty or whitespace-only names exist. |

## 6. Postconditions
- No job title records were created.
- The form remains open for correction.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
