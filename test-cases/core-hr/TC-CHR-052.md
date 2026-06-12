---
id: TC-CHR-052
user_story: US-CHR-005
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-052: HR Officer role can manage job titles

## 1. Test Objective
Verify that a user with the HR Officer role can successfully create, view, edit, and deactivate job titles, confirming this role is authorized per the user story.

## 2. Related Requirements
- User Story: US-CHR-005
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- No job title named "Office Manager" exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized role |
| Title Name | Office Manager | New title to create |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/job-titles` using HR Officer token | Response status is 200 OK. Job titles list is returned. |
| 2 | Call `POST /api/v1/job-titles` with body `{ title_name: "Office Manager" }` using HR Officer token | Response status is 201 Created. |
| 3 | Call `PUT /api/v1/job-titles/{new_id}` with body `{ title_name: "Office Manager", description: "Updated" }` using HR Officer token | Response status is 200 OK. Description is updated. |
| 4 | Call `PATCH /api/v1/job-titles/{new_id}/deactivate` using HR Officer token | Response status is 200 OK. Job title is deactivated. |

## 6. Postconditions
- The job title "Office Manager" was created, updated, and deactivated by the HR Officer.
- All operations succeeded, confirming HR Officer authorization.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
