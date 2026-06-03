---
id: TC-AUTH-044
user_story: US-AUTH-006
module: Authentication
priority: high
type: performance
status: draft
created: 2026-06-03
---

# TC-AUTH-044: System supports 50 custom roles per tenant and 200+ permissions

## 1. Test Objective
Verify that the system can handle the upper boundary of 50 custom roles per tenant and 200+ distinct permissions without degradation. This includes creating the maximum number of roles, assigning a large permission set to a role, and ensuring the roles management UI remains responsive.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-2, FR-3, FR-6
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" is provisioned and in `active` state.
- User `admin@acme.com` is authenticated with `Tenant Admin` role.
- The permission catalog contains at least 200 distinct permissions across all modules.
- No custom roles exist yet (or current count is known and below 50).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Tenant Admin role |
| Custom roles to create | 50 | Named "Custom Role 01" through "Custom Role 50" |
| Permissions per role | Varying (5 to 200) | Last role gets all 200+ permissions |
| Total permissions in catalog | 200+ | Across Leave, Attendance, Payroll, HR, Recruitment, etc. |
| Tenant | acme (acme.yourhrm.com) | Active tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin@acme.com` and obtain JWT. | JWT issued with Tenant Admin role. |
| 2 | Programmatically create 49 custom roles via `POST /api/v1/tenant/roles`, each with 5-10 unique permissions. Name them "Custom Role 01" through "Custom Role 49". | Each request returns HTTP 201 Created. All 49 roles are created successfully. |
| 3 | Create the 50th custom role "Custom Role 50" with all 200+ permissions assigned. | HTTP 201 Created. Role is created with the full permission set. |
| 4 | Send `GET /api/v1/tenant/roles` and verify the complete list. | HTTP 200 OK. Response contains 7 built-in roles + 50 custom roles = 57 total roles. All roles display correct names, permission counts, and user counts. |
| 5 | Verify `GET /api/v1/tenant/roles` response time. | Response returned within 400ms (P95 SLA for read operations). |
| 6 | Attempt to create a 51st custom role (if a limit is enforced). | Either HTTP 201 Created (if no hard limit) or HTTP 400 Bad Request with a message about the custom role limit. Document the actual behavior. |
| 7 | Send `GET /api/v1/tenant/roles/{role_50_id}` to retrieve the role with 200+ permissions. | HTTP 200 OK. All 200+ permissions are returned correctly in the response. |
| 8 | Assign "Custom Role 50" (200+ permissions) to a user via `PATCH /api/v1/tenant/users/{user_id}`. | HTTP 200 OK. Role assignment succeeds. |
| 9 | Authenticate as the user from step 8 and obtain JWT. | JWT issued. The `permissions` claim contains all 200+ permissions. JWT size is within acceptable limits. |
| 10 | Navigate to the Roles management page in the UI. | Page loads within 2.5s. All 57 roles are displayed. Scrolling and filtering are responsive. The permission tree for "Custom Role 50" renders all 200+ permissions with module grouping. |

## 6. Postconditions
- Tenant "acme" has 50 custom roles (plus 7 built-in).
- The role with 200+ permissions is correctly stored and retrievable.
- JWT with a large permission set is issued and parseable.
- System performance remains within SLA under maximum load.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
