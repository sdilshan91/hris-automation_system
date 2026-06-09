---
id: TC-AUTH-061
user_story: US-AUTH-008
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-09
---

# TC-AUTH-061: Target JWT contains only target tenant roles and permissions

## 1. Test Objective
Verify that tenant switching creates a JWT whose role and permission claims are scoped only to the target tenant, including boundary cases where source and target memberships have overlapping or conflicting roles.

## 2. Related Requirements
- User Story: US-AUTH-008
- Acceptance Criteria: AC-5
- Functional Requirements: FR-3, FR-5
- Business Rules: BR-2
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- User is authenticated in tenant A as `Tenant Admin`.
- User has active membership in tenant B as `Employee`.
- Tenant A has permissions `Payroll.View`, `Tenant.Users.Manage`, and `Leave.Approve.All`.
- Tenant B grants only employee permissions, such as `Employee.Profile.View` and `Leave.Request.Create`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Source tenant | acme | Role: Tenant Admin |
| Target tenant | globex | Role: Employee |
| Overlap role fixture | Auditor in both tenants | Permissions differ by tenant |
| Boundary membership count | 10 tenants | User belongs to many tenants |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Decode the source tenant A JWT before switching. | JWT contains tenant A ID and tenant A roles/permissions. |
| 2 | Switch to tenant B with `POST /api/v1/auth/switch-tenant`. | HTTP 200 with new access token. |
| 3 | Decode the new JWT. | `tenant_id` equals tenant B ID only. |
| 4 | Inspect `roles` claims. | Claims include tenant B roles only; `Tenant Admin` from tenant A is absent. |
| 5 | Inspect `permissions` claims. | Claims include tenant B permissions only; source permissions are absent. |
| 6 | Repeat using a user with the same role name in tenant A and tenant B but different permission sets. | JWT permissions match the target tenant role-permission mapping, not the source mapping. |
| 7 | Repeat with a user who has 10 active tenant memberships. | JWT includes only the selected target tenant context and does not include roles from the other 9 memberships. |
| 8 | Use tenant B JWT against a tenant A admin-only endpoint under tenant A host. | Request is rejected because tenant context and claims do not authorize tenant A admin access. |

## 6. Postconditions
- Target JWT and authorization behavior prove per-membership role isolation.
- No source tenant claims are present after switch.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
