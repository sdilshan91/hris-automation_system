---
id: TC-REC-ISO-019
user_story: US-REC-010
module: Recruitment
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-REC-ISO-019: Converting in Tenant A creates the employee (+ user/user_tenant + applicant link + vacancy update) visible ONLY in Tenant A; cross-tenant conversion blocked; all rows session-stamped (AC-5, NFR-2)

## 1. Test Objective
Verify AC-5 / NFR-2 for REC-010's NEW multi-table mutation: a conversion performed in Tenant A creates the `employee` (and, when enabled, `User`/`UserTenant`/`user_tenant_role`), sets the applicant link, and increments the vacancy — and NONE of it is visible to Tenant B. A user in Tenant B cannot read Tenant A's newly created employee, and cannot convert a Tenant A applicant (the EF global query filter prevents loading A's applicant/offer/vacancy under B's session). Every row written by the conversion carries the SESSION-derived `tenant_id` (via TenantInterceptor) — a body-injected `tenant_id` is ignored. This exercises EF Core global query filters across the FULL conversion graph (employee + applicant + vacancy + user_tenant). The generic no/invalid/mismatched tenant-context rejection and the cross-tenant write-block + body-injected-tenant_id contract are REUSED from TC-REC-ISO-010/011 on the recruitment surface (per the module's ISO-reuse convention).

NOTE: AC-5/NFR-2 specify PostgreSQL RLS on the `employee` table; the platform enforces isolation via EF Core global query filters + TenantInterceptor. ISO TCs describe the EF mechanism and note RLS session-level assertion as an extension point if RLS is later added on `employee`. The `User` is a global identity, but the `UserTenant`/role membership and the recruitment + employee rows are tenant-scoped — this case asserts the TENANT-SCOPED rows do not leak.

## 2. Related Requirements
- User Story: US-REC-010
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-2 (all conversion data tenant-scoped + RLS)
- Functional Requirements: FR-4/FR-6/FR-7 (employee + link + vacancy rows session-stamped), FR-5 (user_tenant/role tenant-scoped)
- Reuses: TC-REC-ISO-010 (no/invalid/mismatched tenant context), TC-REC-ISO-011 (cross-tenant write block + body-injected tenant_id) on the recruitment surface.

## 3. Preconditions
- Tenant "acme" (A): applicant {acme_applicantId} Hired with Accepted offer on vacancy {acme_vacancyId}.
- Tenant "globex" (B): `hr@globex` with `Recruitment.Manage.All` + `Employee.Create.All`; a globex Hired+Accepted applicant {globex_applicantId} for the valid control.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | conversion happens here |
| Tenant B | globex | must see nothing of A's conversion |
| A applicant | {acme_applicantId} | Hired + Accepted |
| Injected tenant_id | acme's id in B's request body | must be ignored |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As an acme HR user, convert {acme_applicantId} -> employee {acme_newEmployeeId} (+ user_tenant, applicant link, filled_count++) | Succeeds; all rows stamped tenant_id=acme. |
| 2 | As `hr@globex`, list/query employees | {acme_newEmployeeId} is NOT returned; globex sees zero of acme's employees (EF global query filter) (AC-5). |
| 3 | As `hr@globex`, `GET` employee {acme_newEmployeeId} directly | 404; acme's new employee is not retrievable cross-tenant. |
| 4 | As `hr@globex`, attempt to convert acme's applicant {acme_applicantId} | 404/403; the EF filter prevents loading acme's applicant/offer/vacancy; no employee/user_tenant/link/increment written in acme (reuses TC-REC-ISO-011). |
| 5 | As `hr@globex`, with no/invalid/mismatched tenant context, call the convert endpoint | Rejected (no tenant context resolved); no cross-tenant read/write (reuses TC-REC-ISO-010). |
| 6 | As `hr@globex`, convert a VALID globex applicant {globex_applicantId} but inject `tenant_id=acme` in the body | The body tenant_id is ignored; the new employee + user_tenant + applicant link + vacancy update are all stamped globex (TenantInterceptor), never acme (reuses TC-REC-ISO-011). |
| 7 | Verify at the DB level | `SELECT * FROM employee WHERE tenant_id = globex_id` returns only globex rows; acme's converted employee is invisible under globex; the applicant link + vacancy filled_count for acme are unchanged by any globex action. (If RLS exists on `employee`, confirm a globex session cannot read acme rows via direct SQL.) |

## 6. Postconditions
- No cross-tenant employee/user_tenant/applicant/vacancy data was read or written; every conversion row carries the session tenant_id.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
