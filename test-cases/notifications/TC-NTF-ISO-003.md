---
id: TC-NTF-ISO-003
user_story: US-NTF-001
module: Notifications & Audit
priority: critical
type: security
status: pass
created: 2026-06-17
---

# TC-NTF-ISO-003: EF query filter blocks cross-tenant reads; writes tenant-stamped (Postgres RLS deferred)

## 1. Test Objective
Verify the `notification` table is tenant-isolated at the data layer: EF Core global query filters
prevent reading other tenants' notification rows, and the `TenantInterceptor` stamps tenant_id on
inserts from the authenticated session. Documents that PostgreSQL RLS named in NFR-2 is a deferred
platform extension; the test asserts the EF mechanism in force today.

## 2. Related Requirements
- User Story: US-NTF-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-3 (tenant-scoped persistence)
- Non-Functional: NFR-2 (tenant isolation; RLS deferred -> EF query filters + TenantInterceptor)

## 3. Preconditions
- Tenant A and Tenant B each have notification rows.
- App runs with ITenantContext resolved per request.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A rows | 5 | tenant_id=TA |
| Tenant B rows | 3 | tenant_id=TB |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With ITenantContext = Tenant A, query the notification set via EF | Only the 5 Tenant A rows returned; Tenant B rows filtered out |
| 2 | With ITenantContext = Tenant B, query the notification set | Only the 3 Tenant B rows returned |
| 3 | As Tenant A, insert a notification without specifying tenant_id | `TenantInterceptor` stamps tenant_id = Tenant A automatically |
| 4 | As Tenant A, attempt to insert a row asserting tenant_id = Tenant B | tenant_id is overwritten/stamped to Tenant A (client value not trusted) |
| 5 | (Deferred / conditional) Run raw SQL without `app.current_tenant_id` set | RLS expectation "zero rows" is CONDITIONAL — Postgres RLS is not yet enabled; reword as future hardening. Today isolation is enforced by EF filters + interceptor (steps 1-4) |

## 6. Postconditions
- Reads scoped to the active tenant; writes stamped server-side; RLS flagged as deferred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
