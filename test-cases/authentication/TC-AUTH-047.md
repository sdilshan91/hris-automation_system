---
id: TC-AUTH-047
user_story: US-AUTH-006
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-03
---

# TC-AUTH-047: Redis cache invalidation on role or permission change

## 1. Test Objective
Verify that when a user's role assignment is changed, or when a role's permissions are modified, the corresponding Redis cache entry (`t:{tenantId}:user:{userId}:permissions`) is invalidated. The next request or token refresh must fetch fresh permission data from the database, not stale cached data.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3, FR-4
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" is provisioned and in `active` state.
- User `admin@acme.com` is authenticated with `Tenant Admin` role.
- User `cached-user@acme.com` has the "Employee" role and an active session.
- Redis is running and the cache key `t:{acme_tenant_id}:user:{cached_user_id}:permissions` is populated with the Employee permission set.
- A custom role "Data Analyst" exists with permission `Report.View`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Tenant Admin role |
| Target user | cached-user@acme.com | Employee role, active session |
| Redis cache key | t:{acme_tenant_id}:user:{cached_user_id}:permissions | Permission cache for target user |
| Custom role | Data Analyst | Has Report.View permission |
| New permission to add | Attendance.View | Will be added to Data Analyst role |
| Tenant | acme (acme.yourhrm.com) | Active tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin@acme.com` and obtain JWT. | JWT issued with Tenant Admin role. |
| 2 | Verify Redis cache exists: inspect key `t:{acme_tenant_id}:user:{cached_user_id}:permissions`. | Cache key exists with Employee-level permissions. |
| 3 | Assign "Data Analyst" role to `cached-user@acme.com` via `PATCH /api/v1/tenant/users/{cached_user_id}` with `{ "roleIds": ["{employee_role_id}", "{data_analyst_role_id}"] }`. | HTTP 200 OK. Role assignment updated. |
| 4 | Immediately check Redis cache key `t:{acme_tenant_id}:user:{cached_user_id}:permissions`. | Cache key has been invalidated (deleted or updated). The stale Employee-only permission set is no longer cached. |
| 5 | Trigger a token refresh for `cached-user@acme.com`. | New JWT issued with `roles: ["Employee", "Data Analyst"]` and `permissions` including `Report.View`. The refresh fetched fresh data from the database (or repopulated cache). |
| 6 | Verify Redis cache is repopulated with the updated permissions. | Cache key `t:{acme_tenant_id}:user:{cached_user_id}:permissions` now contains the union of Employee and Data Analyst permissions. |
| 7 | Now modify the "Data Analyst" role: send `PUT /api/v1/tenant/roles/{data_analyst_role_id}` adding `Attendance.View` to its permissions. | HTTP 200 OK. Role updated with new permission. |
| 8 | Check Redis cache for ALL users who hold the "Data Analyst" role. | Cache keys for all affected users (at minimum `cached-user@acme.com`) have been invalidated. |
| 9 | Trigger token refresh for `cached-user@acme.com` again. | New JWT includes `Attendance.View` in the permissions claim (reflecting the role's updated permission set). |
| 10 | Verify that cache invalidation occurred within an acceptable time window. | Cache invalidation happens synchronously with the role/permission change API response (no delayed propagation beyond the write SLA of 800ms). |

## 6. Postconditions
- Redis cache accurately reflects the current permission state for all affected users.
- No stale permission data is served from cache after role or permission changes.
- The system's permission evaluation uses the fresh data on the next token refresh.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
