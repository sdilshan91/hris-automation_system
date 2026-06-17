---
id: TC-ADM-ISO-031
user_story: US-ADM-010
module: Admin Console
priority: medium
type: security
status: blocked
created: 2026-06-17
---

# TC-ADM-ISO-031: [DEFERRED] PostgreSQL RLS DB-layer isolation for the export pipeline

## 1. Test Objective
Cover the deferred defense-in-depth DB-layer isolation for the export pipeline: a PostgreSQL row-level-security policy keyed on `app.current_tenant_id` that blocks raw cross-tenant reads of tenant data (and the `ExportRequest` table) at the database layer, beneath the app + EF guards.

DEFERRED — status: blocked. AC-5 names PostgreSQL RLS as a third isolation layer, but this platform enforces tenant isolation via the app (`ITenantContext`) + EF (global query filter / `TenantInterceptor`) layers only; RLS is a deferred platform extension point (same family as US-ADM-001..009 / Payroll / Leave). The app + EF isolation on the export path is run-green (TC-ADM-ISO-028/-029/-030).

## 2. Related Requirements
- User Story: US-ADM-010
- Acceptance Criteria: AC-5 ("PostgreSQL RLS prevents any cross-tenant data access" — DB-layer hardening)
- (Platform) RLS deferral family — see US-ADM-001 AC-6/FR-6 and TC-ADM-ISO-027

## 3. Preconditions
- (Deferred prerequisite) RLS policies on tenant-scoped tables (incl. `export_request`) keyed on `app.current_tenant_id`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| RLS session var | app.current_tenant_id | not set today |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm app+EF isolation today | Export path is tenant-scoped via ITenantContext + EF query filter + TenantInterceptor (TC-ADM-ISO-028/-029/-030). |
| 2 | (When RLS implemented) Raw SQL on a tenant table / export_request without `app.current_tenant_id` set | Returns zero rows (RLS blocks). |
| 3 | (When RLS implemented) Raw SQL with another tenant's id set | Returns only that tenant's rows. |
| 4 | Until implemented | Expected behavior: "Not available — PostgreSQL RLS is deferred platform infra; isolation today is app + EF (TC-ADM-ISO-028/-029/-030)." |

## 6. Postconditions
- Deferred; no RLS asserted today.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
