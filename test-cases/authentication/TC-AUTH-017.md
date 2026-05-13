---
id: TC-AUTH-017
user_story: US-AUTH-006
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-017: User without Admin role is denied admin access

## 1. Test Objective
Verify that a user with only the "Employee" role is denied access to admin-only and HR-only endpoints, receiving 403 Forbidden with appropriate error details.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1, FR-5
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- User `employee@acme.com` is authenticated in tenant "acme" with the "Employee" role only.
- The JWT contains `roles: ["Employee"]` with limited permissions.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | employee@acme.com | Employee role only |
| Role | Employee | Limited permissions |
| Admin endpoint | GET /api/v1/tenant/users | Requires User.Manage permission |
| HR endpoint | GET /api/v1/tenant/payroll/runs | Requires Payroll.View permission |
| Allowed endpoint | GET /api/v1/tenant/me/profile | Available to all authenticated users |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `employee@acme.com` and obtain JWT | JWT contains `roles: ["Employee"]` with employee-level permissions only. |
| 2 | Send `GET /api/v1/tenant/users` (admin endpoint) | HTTP 403 Forbidden; user lacks `User.Manage` permission. |
| 3 | Verify the response body indicates insufficient permissions | Error message such as "You do not have permission to access this resource." |
| 4 | Send `GET /api/v1/tenant/payroll/runs` (HR endpoint) | HTTP 403 Forbidden; user lacks `Payroll.View` permission. |
| 5 | Send `POST /api/v1/tenant/roles` (role management) | HTTP 403 Forbidden; user lacks `Role.Manage` permission. |
| 6 | Send `DELETE /api/v1/tenant/roles/{id}` (role deletion) | HTTP 403 Forbidden. |
| 7 | Send `GET /api/v1/tenant/me/profile` (self-service endpoint) | HTTP 200 OK; employee can access their own profile. |
| 8 | Verify authorization failure is logged with details | Log entry contains user_id, endpoint, missing permission for security monitoring. |
| 9 | Verify the UI shows a clean 403 page | "You don't have permission to access this page" with a "Go to Dashboard" link. |

## 6. Postconditions
- Admin/HR endpoints remain inaccessible to the Employee user.
- Self-service endpoints are accessible.
- Authorization failure events are logged for security monitoring.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
