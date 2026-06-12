---
id: TC-CHR-051
user_story: US-CHR-005
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-051: Unauthorized role (Employee) cannot manage job titles -- 403

## 1. Test Objective
Verify that a user with an unauthorized role (e.g., Employee, a standard non-admin role) receives a 403 Forbidden response when attempting to create, edit, or deactivate job titles. Only Tenant Admin and HR Officer roles are authorized per the user story.

## 2. Related Requirements
- User Story: US-CHR-005
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with only the "Employee" role is authenticated in the "acme" tenant context.
- At least one job title exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Employee | Not authorized for job title management |
| Existing Title ID | {valid UUID} | An existing job title in acme |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `POST /api/v1/job-titles` with valid body `{ title_name: "New Title" }` using Employee role token | Response status is 403 Forbidden. |
| 2 | Call `PUT /api/v1/job-titles/{job_title_id}` with valid body using Employee role token | Response status is 403 Forbidden. |
| 3 | Call `PATCH /api/v1/job-titles/{job_title_id}/deactivate` using Employee role token | Response status is 403 Forbidden. |
| 4 | Call `GET /api/v1/job-titles` using Employee role token | Response may return 200 OK (view may be permitted) or 403 (if view is also restricted). Document actual behavior and verify it aligns with permission catalog. |
| 5 | Verify the response body does not leak sensitive error details (e.g., stack traces) | Response contains a generic "Access denied" or "Forbidden" message. |

## 6. Postconditions
- No job title records were created, modified, or deactivated.
- The system correctly enforced role-based authorization.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
