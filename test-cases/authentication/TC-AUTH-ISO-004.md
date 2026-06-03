---
id: TC-AUTH-ISO-004
user_story: US-AUTH-006
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-03
---

# TC-AUTH-ISO-004: RBAC cross-tenant isolation -- roles, permissions, and cache keys are tenant-scoped

## 1. Test Objective
Verify complete multi-tenant isolation for RBAC data: (1) custom roles created in tenant A are never visible in tenant B's role queries, (2) the roles API endpoint rejects requests without a valid tenant context, (3) PostgreSQL RLS blocks direct database queries that attempt to read roles across tenants, (4) Redis cache keys are tenant-scoped so that permission data from one tenant cannot leak into another, and (5) JWT claims are strictly scoped to the authenticated tenant.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-7
- Functional Requirements: FR-2, FR-10
- Business Rules: BR-1
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Two tenants exist: "acme" (tenant A) and "globex" (tenant B), both in `active` state.
- Tenant A has a custom role "Acme Analyst" with permissions: `Report.View`, `Report.Export`.
- Tenant B has a custom role "Globex Reviewer" with permissions: `Recruitment.Review`.
- User `cross@acme.com` has memberships in both tenants:
  - Tenant A: roles = Employee, Acme Analyst
  - Tenant B: roles = Employee
- Redis is running with tenant-scoped cache keys.
- Database-level access is available for RLS verification (DBA or test harness with direct DB connection).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme (acme.yourhrm.com) | Has "Acme Analyst" custom role |
| Tenant B | globex (globex.yourhrm.com) | Has "Globex Reviewer" custom role |
| Cross-tenant user | cross@acme.com | Member of both tenants |
| Tenant A admin | admin-a@acme.com | Tenant Admin in acme |
| Tenant B admin | admin-b@globex.com | Tenant Admin in globex |
| Acme Analyst role ID | {acme_analyst_role_id} | Custom role in tenant A |
| Globex Reviewer role ID | {globex_reviewer_role_id} | Custom role in tenant B |
| Redis key pattern (A) | t:{acme_tenant_id}:user:{user_id}:permissions | Tenant A cache |
| Redis key pattern (B) | t:{globex_tenant_id}:user:{user_id}:permissions | Tenant B cache |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **API isolation -- Tenant A roles not visible in Tenant B:** Authenticate as `admin-b@globex.com` at `globex.yourhrm.com`. Send `GET /api/v1/tenant/roles`. | HTTP 200 OK. Response lists globex's built-in roles and "Globex Reviewer". "Acme Analyst" does NOT appear. No acme-specific roles or permissions leak into the response. |
| 2 | **API isolation -- Tenant B roles not visible in Tenant A:** Authenticate as `admin-a@acme.com` at `acme.yourhrm.com`. Send `GET /api/v1/tenant/roles`. | HTTP 200 OK. Response lists acme's built-in roles and "Acme Analyst". "Globex Reviewer" does NOT appear. |
| 3 | **Cross-tenant role assignment blocked:** As `admin-b@globex.com`, attempt `PATCH /api/v1/tenant/users/{cross_user_tenant_b_id}` with `{ "roleIds": ["{acme_analyst_role_id}"] }` (assigning an acme role to a globex membership). | HTTP 400 Bad Request or 404 Not Found. The acme role ID is not valid in the globex tenant context. Error message indicates the role does not exist (in this tenant). |
| 4 | **Cross-tenant role fetch blocked:** As `admin-b@globex.com`, attempt `GET /api/v1/tenant/roles/{acme_analyst_role_id}`. | HTTP 404 Not Found. The acme role ID is not accessible from the globex tenant context (EF Core global filter / RLS). |
| 5 | **Missing tenant context rejected:** Send `GET /api/v1/tenant/roles` without a valid tenant context (no subdomain resolution, missing/invalid `X-Tenant-Id` header, or a forged tenant claim). | HTTP 401 Unauthorized or 400 Bad Request. API rejects the request because it cannot determine the tenant scope. |
| 6 | **JWT tenant scoping -- Tenant A JWT:** Authenticate as `cross@acme.com` at `acme.yourhrm.com`. Decode the JWT. | `tenant_id` = acme UUID. `roles` = `["Employee", "Acme Analyst"]`. `permissions` include `Report.View`, `Report.Export`. No globex roles or permissions present. |
| 7 | **JWT tenant scoping -- Tenant B JWT:** Authenticate as `cross@acme.com` at `globex.yourhrm.com`. Decode the JWT. | `tenant_id` = globex UUID. `roles` = `["Employee"]`. `permissions` include only Employee-level permissions. No `Report.View`, `Report.Export`, or "Acme Analyst" role. |
| 8 | **JWT from Tenant A rejected in Tenant B:** Use the Tenant A JWT (from step 6) to send `GET /api/v1/tenant/roles` at `globex.yourhrm.com`. | HTTP 401 Unauthorized or 403 Forbidden. The middleware detects that the JWT's `tenant_id` claim does not match the resolved tenant from the subdomain. |
| 9 | **Redis cache isolation:** Inspect Redis keys for `cross@acme.com`. | Two separate cache keys exist: `t:{acme_tenant_id}:user:{cross_user_id}:permissions` (with Report.View, Report.Export) and `t:{globex_tenant_id}:user:{cross_user_id}:permissions` (with Employee-only permissions). The keys are distinct and contain different permission sets. |
| 10 | **RLS verification:** Using a direct database connection with the globex tenant context set (e.g., `SET app.current_tenant_id = '{globex_tenant_id}'`), execute `SELECT * FROM role WHERE tenant_id = '{acme_tenant_id}'`. | Query returns 0 rows. PostgreSQL RLS policy prevents reading acme's roles even with a direct SQL query when the session is in globex's context. |
| 11 | **RLS verification (reverse):** Set tenant context to acme and query globex roles. | Query returns 0 rows. Isolation is bidirectional. |
| 12 | **Role name collision across tenants:** Create a custom role named "Data Analyst" in both tenant A and tenant B (same name, different role_ids and different permissions). | Both creations succeed (HTTP 201). Role names are unique within a tenant but can be reused across tenants. Each tenant's "Data Analyst" has its own role_id and independent permissions. |

## 6. Postconditions
- Complete isolation verified: no RBAC data leaks between tenants via API, JWT, cache, or database.
- Cross-tenant role assignment and access are rejected at multiple layers.
- Redis cache keys are correctly namespaced by tenant.
- RLS enforces row-level isolation at the database layer.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
