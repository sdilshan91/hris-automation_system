---
id: TC-ONB-ISO-003
user_story: US-ONB-001
module: Onboarding / Offboarding
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-003: EF query filter blocks cross-tenant reads; writes are tenant-stamped (RLS deferred)

## 1. Test Objective
Verify the read/write isolation layers in force: (a) the EF Core global query filter blocks any cross-tenant read of `onboarding_template`/tasks at the data layer, and (b) the `TenantInterceptor` auto-stamps `TenantId` on every newly created template and task from the resolved context, regardless of body content (FR-5). The story's Postgres-RLS expectation (NFR-2) is documented as CONDITIONAL/deferred.

## 2. Related Requirements
- User Story: US-ONB-001
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5
- Non-Functional Requirements: NFR-2 (EF query filter + interceptor today; RLS deferred)
- Cross-cutting: mandatory multi-tenant isolation

## 3. Preconditions
- Tenants `acme` and `globex` exist with seeded templates.
- Access to a test harness that can exercise the repository/DbContext under a set `ITenantContext`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Context tenant | acme | scopes all reads/writes |
| Foreign data | globex templates + tasks | must be invisible |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With `ITenantContext` = acme, query all `onboarding_template` rows via the DbContext | Only acme rows returned; globex rows excluded by the global query filter — no explicit WHERE needed in the handler. |
| 2 | With context = acme, create a template+tasks WITHOUT setting TenantId in code | On SaveChanges, the `TenantInterceptor` stamps `TenantId = acme` on the template and all tasks (FR-5). |
| 3 | Switch context to globex and re-query | The acme template created in step 2 is NOT visible; globex sees only its own rows. |
| 4 | (CONDITIONAL — deferred) Run a raw SQL read without `app.current_tenant_id` set | DOCUMENTED EXPECTATION once Postgres RLS lands: zero rows. Today RLS is NOT wired; mark this step blocked/deferred and rely on steps 1-3 for the in-force isolation guarantee. Do NOT fabricate an RLS pass. |

## 6. Postconditions
- Cross-tenant reads are filtered at the data layer; writes are tenant-stamped from context; the RLS layer is honestly recorded as deferred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
