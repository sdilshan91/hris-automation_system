---
id: TC-PAY-ISO-001
user_story: US-PAY-001
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-001: Tenant A cannot see or retrieve Tenant B's salary components/structures (cross-tenant read isolation)

## 1. Test Objective
Verify AC-6 / FR-8: salary components and structures are fully tenant-isolated on reads. A user authenticated in Tenant A cannot list or retrieve any salary component, structure, or structure-component link belonging to Tenant B. This exercises the codebase's tenant-isolation mechanism (EF Core global query filters + TenantInterceptor). (Note: US-PAY-001 AC-6/FR-8 specify PostgreSQL RLS policies on the payroll tables; this platform currently enforces isolation via EF Core global query filters. If RLS policies are later added on `salary_component`/`salary_structure`, extend Step 4 to assert isolation at the DB session level as defense-in-depth.)

## 2. Related Requirements
- User Story: US-PAY-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-8
- Data Requirements: S7 (tenant_id discriminator + RLS policy)

## 3. Preconditions
- Tenant "acme" has components (incl. "Acme Basic", code ABASIC) and a structure ("Acme FT").
- Tenant "globex" has components (incl. "Globex Basic", code GBASIC) and a structure ("Globex FT").
- An HR Officer with `Payroll.*.All` is authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has ABASIC + Acme FT |
| Tenant B | globex | Has GBASIC + Globex FT |
| Auth context | acme | HR authenticated in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in acme; JWT carries acme `tenant_id` | Tenant context resolves to acme. |
| 2 | `GET /api/v1/payroll/components` and `GET .../structures` | Responses contain only acme records; zero globex components/structures (AC-6). |
| 3 | `GET .../components/{globex_component_id}` and `.../structures/{globex_structure_id}` using globex UUIDs | 404 Not Found (global query filter excludes them); never 200 with another tenant's data. |
| 4 | Verify at the database level | `SELECT * FROM salary_component WHERE tenant_id = acme_id` returns only acme rows; `... = globex_id` returns only globex rows. (If an RLS policy exists, confirm a session set to acme cannot read globex rows even via a direct query.) |
| 5 | Switch to globex context and repeat list/fetch | globex sees only its own components/structures; zero acme records. |

## 6. Postconditions
- No cross-tenant payroll configuration data exposed via API or query.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
