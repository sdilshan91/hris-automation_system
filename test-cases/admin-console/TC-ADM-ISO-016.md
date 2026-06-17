---
id: TC-ADM-ISO-016
user_story: US-ADM-006
module: Admin Console
priority: medium
type: security
status: blocked
created: 2026-06-17
---

# TC-ADM-ISO-016: [DEFERRED] PostgreSQL RLS layer for settings isolation (AC-5 "RLS at DB layer")

## 1. Test Objective
Verify AC-5's stated PostgreSQL RLS layer: a raw SQL query issued without the `app.current_tenant_id` session variable returns zero cross-tenant settings rows (DB-enforced isolation independent of the app/EF layers). **DEFERRED** — this codebase enforces tenant isolation via EF Core global query filters (read) + `TenantInterceptor` (write stamping), NOT Postgres RLS. RLS is a documented deferred platform extension point (same family as the deferred RLS across US-ADM-001..005, Payroll, Leave). App + EF-layer isolation for settings is covered run-green by TC-ADM-006-14 and TC-ADM-ISO-014/-015.

## 2. Related Requirements
- User Story: US-ADM-006
- Acceptance Criteria: AC-5 ("PostgreSQL RLS blocks any cross-tenant access at the database layer")
- STORY MISMATCH: AC-5 names Postgres RLS as the DB-layer mechanism; the platform implements only the app (`ITenantContext`) + EF (global query filter / `TenantInterceptor`) layers today.

## 3. Preconditions
- Postgres RLS policies + `app.current_tenant_id` session GUC configured — NOT present in the current platform.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| isolation layer | Postgres RLS | deferred |
| in force today | EF query filters + TenantInterceptor + ITenantContext-only settings | TC-ADM-006-14, ISO-014/-015 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open a DB session WITHOUT `app.current_tenant_id`; `SELECT` tenant settings/branding columns | DEFERRED — once RLS lands: zero cross-tenant rows. Today: the app never issues unscoped queries; EF filters + `ITenantContext`-only settings enforce isolation at the query level. |
| 2 | Set `app.current_tenant_id = <Acme>`; query settings | DEFERRED — expected: only Acme rows. |
| 3 | Until RLS is implemented | Rely on TC-ADM-006-14 (authz) + TC-ADM-ISO-014 (ITenantContext-only, 404/empty) + TC-ADM-ISO-015 (branding path isolation). |

## 6. Postconditions
- No mutation. Stays `blocked` pending Postgres RLS hardening; recommend rewording AC-5 to specify EF query filters + `ITenantContext` as the active layer with RLS as future hardening.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
