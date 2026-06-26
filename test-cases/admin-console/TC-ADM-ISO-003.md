---
id: TC-ADM-ISO-003
user_story: US-ADM-001
module: Admin Console
priority: critical
type: security
status: blocked
created: 2026-06-16
---

# TC-ADM-ISO-003: EF Core global query filter blocks cross-tenant reads; writes are tenant-stamped (RLS deferred)

## 1. Test Objective
Verify the read/write isolation mechanism this platform actually uses for the new tenant's data: the EF Core global query filter (`TenantId == _tenantContext.TenantId` in `AppDbContext.OnModelCreating`) scopes every tenant-scoped read, and the `TenantInterceptor` auto-stamps `TenantId` on inserts so a provisioning/write operation cannot place rows in another tenant. An injected `tenant_id` in a request body is ignored in favour of the server-resolved tenant.

> Platform accuracy note: AC-6 / FR-6 of US-ADM-001 specify PostgreSQL RLS + `app.current_tenant_id`. This codebase enforces isolation via EF global query filters + TenantInterceptor, NOT RLS — RLS is a deferred platform extension point. This test asserts the EF mechanism that is in force today and notes RLS as the future hardening layer. (The story's "raw SQL without app.current_tenant_id returns zero rows" RLS-verification hint is therefore CONDITIONAL/deferred.)

## 2. Related Requirements
- User Story: US-ADM-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-3 (transactional create, tenant-stamped), FR-6 (EF query filter today; RLS deferred)
- Cross-cutting: mandatory multi-tenant isolation

## 3. Preconditions
- Tenant A (`alpha`) freshly provisioned; Tenant B (`beta`) pre-exists.
- Access to a repository/query path through `AppDbContext` under a known tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Read probe | tenant-scoped entity query under alpha context | should exclude beta |
| Write probe | create a child entity for alpha with `tenant_id = beta` injected in body | interceptor must override |
| IgnoreQueryFilters | only the tenant-resolution lookup uses it deliberately | not for business reads |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Under the alpha tenant context, query a tenant-scoped entity (e.g. leave types) via the normal repository path | The global query filter appends `WHERE tenant_id = @alpha`; only alpha rows return; beta rows are unreachable without `IgnoreQueryFilters()`. |
| 2 | Under the alpha context, insert a new tenant-scoped row with `tenant_id` set to beta's id in the request payload | The `TenantInterceptor` stamps `tenant_id = alpha` on save; the injected beta value is ignored. The row belongs to alpha. |
| 3 | Switch to the beta context and re-query | Beta sees only its own rows; the row created in step 2 is NOT visible to beta (it is alpha's). |
| 4 | Confirm `IgnoreQueryFilters()` is used only by deliberate platform paths (e.g. tenant-subdomain resolution), not by business queries touching the new tenant's data | Business reads remain filtered; no business code bypasses the filter for the new tenant. |
| 5 | Record the RLS-deferred note | The story's raw-SQL RLS check is documented as a deferred extension point; isolation is asserted at the EF layer. |

## 6. Postconditions
- Cross-tenant reads are filtered out and writes are tenant-stamped for the newly provisioned tenant via EF; RLS remains a documented future hardening layer.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
