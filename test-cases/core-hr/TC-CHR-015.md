---
id: TC-CHR-015
user_story: US-CHR-004
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-CHR-015: HR Officer role can manage departments

## 1. Test Objective
Verify that a user with the HR Officer role (not Tenant Admin) can successfully create, edit, and deactivate departments, confirming the user story's dual-role authorization (Tenant Admin OR HR Officer).

## 2. Related Requirements
- User Story: US-CHR-004
- Preconditions: Section 2 of user story (Tenant Admin or HR Officer)
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with only the "HR Officer" role is authenticated in the "acme" tenant context.
- No existing department named "HR Operations" in "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized role |
| User Email | hr@acme.com | HR Officer user |
| Department Name | HR Operations | New department |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `hr@acme.com` with HR Officer role | Login succeeds; JWT contains `roles: ["HR Officer"]`. |
| 2 | Navigate to the Departments management page | Page loads; "Add Department" button is visible and enabled. |
| 3 | Click "Add Department" and create "HR Operations" | API call `POST /api/v1/departments` returns 201 Created. Department appears in the list. |
| 4 | Click Edit on "HR Operations" and change description | API call `PUT /api/v1/departments/{id}` returns 200 OK. Changes are persisted. |
| 5 | Click Deactivate on "HR Operations" (zero employees) | API call to deactivate returns 200 OK. Department is deactivated. |
| 6 | Verify all three operations succeeded | HR Officer has full CRUD access to departments within their tenant. |

## 6. Postconditions
- "HR Operations" was created, updated, and deactivated by an HR Officer.
- All operations were properly audited.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
