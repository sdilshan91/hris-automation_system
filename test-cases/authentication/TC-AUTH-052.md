---
id: TC-AUTH-052
user_story: US-AUTH-007
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-09
---

# TC-AUTH-052: Admin subdomain establishes system context with authorization enforcement

## 1. Test Objective
Verify that `admin.yourhrm.com` resolves to system context only, and that cross-tenant system operations remain limited to authorized System Admin users.

## 2. Related Requirements
- User Story: US-AUTH-007
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1, FR-3, FR-4, FR-6
- Business Rules: BR-3, BR-4

## 3. Preconditions
- `admin` is configured as the system tenant subdomain.
- User `sysadmin@yourhrm.com` has an authorized System Super Admin role.
- User `admin@acme.com` has a tenant admin role only and no system role.
- Tenants `acme` and `globex` exist with active status.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| System host | admin.yourhrm.com | Special system context route |
| Authorized user | sysadmin@yourhrm.com | System Super Admin |
| Unauthorized user | admin@acme.com | Tenant Admin only |
| Protected endpoint | GET /api/v1/system/tenants | Representative cross-tenant operation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send a request to `https://admin.yourhrm.com/api/v1/system/tenants` with no token. | Tenant resolution sets `IsSystemContext = true`; authentication still rejects the request with HTTP 401. |
| 2 | Authenticate as `sysadmin@yourhrm.com` and repeat the request. | HTTP 200 OK. Response may include cross-tenant system data permitted for the system role. |
| 3 | Verify `ITenantContext` for the authorized request. | `IsSystemContext = true`, regular `TenantId` is empty or the configured system tenant ID, and no regular tenant subdomain is set. |
| 4 | Authenticate as `admin@acme.com` and call the same endpoint on `admin.yourhrm.com`. | HTTP 403 Forbidden. Tenant Admin privileges from `acme` do not grant system operations. |
| 5 | Verify audit and security logs for the forbidden request. | Log includes actor, route, denial reason, and system context without leaking tenant data. |
| 6 | Attempt to suspend or terminate the system tenant through normal tenant status APIs. | Operation is rejected because the system tenant cannot be suspended or terminated through normal flows. |

## 6. Postconditions
- System context is used only for the admin subdomain.
- Tenant-scoped roles cannot perform system operations.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
