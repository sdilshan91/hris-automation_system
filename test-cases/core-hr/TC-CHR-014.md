---
id: TC-CHR-014
user_story: US-CHR-004
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-CHR-014: Unauthorized role (Employee) cannot create departments -- 403 Forbidden

## 1. Test Objective
Verify that a user with only the Employee role (not Tenant Admin or HR Officer) is denied access to create departments, receiving a 403 Forbidden response. Only Tenant Admin and HR Officer roles should have department management permissions.

## 2. Related Requirements
- User Story: US-CHR-004
- Preconditions: Section 2 of user story (role requirement)
- Functional Requirements: FR-1 (scoped CRUD)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with only the "Employee" role is authenticated in the "acme" tenant context.
- No Tenant Admin or HR Officer role is assigned to this user.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Employee | NOT authorized for department management |
| User Email | employee@acme.com | Regular employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `employee@acme.com` with Employee role | Login succeeds; JWT contains `roles: ["Employee"]`. |
| 2 | Attempt to navigate to the Departments management page | Either: (a) navigation is blocked and user is redirected to dashboard with "Access Denied" message, or (b) the page loads but "Add Department" button is hidden/disabled. |
| 3 | Attempt direct API call `POST /api/v1/departments` with body `{ name: "Hack Dept" }` using the Employee's JWT | Response status is 403 Forbidden. |
| 4 | Verify response body contains an authorization error message | Message indicates insufficient permissions. |
| 5 | Attempt direct API call `PUT /api/v1/departments/{existing_id}` | Response status is 403 Forbidden. |
| 6 | Attempt direct API call `DELETE /api/v1/departments/{existing_id}` or deactivate endpoint | Response status is 403 Forbidden. |
| 7 | Verify no department was created or modified | Database state is unchanged. |

## 6. Postconditions
- No department was created, modified, or deleted by the unauthorized user.
- Authorization enforcement is consistent across all department CRUD endpoints.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
