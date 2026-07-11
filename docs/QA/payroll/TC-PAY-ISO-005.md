---
id: TC-PAY-ISO-005
user_story: US-PAY-002
module: Payroll
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-PAY-ISO-005: Tenant B cannot access Tenant A's employee salary assignment / revision history (cross-tenant read isolation)

## 1. Test Objective
Verify AC-5 / FR-8: employee salary assignments (`employee_salary_component`) and salary revision history are fully tenant-isolated on reads. A user authenticated in Tenant B cannot list or retrieve any salary assignment, component row, or revision history belonging to an employee in Tenant A. Enforced via EF Core global query filters + TenantInterceptor. (US-PAY-002 AC-5/FR-8 specify PostgreSQL RLS; this platform enforces isolation via EF Core global query filters. If RLS policies are later added on `employee_salary_component`/`salary_revision_history`, extend Step 4 to assert isolation at the DB session level as defense-in-depth.)

## 2. Related Requirements
- User Story: US-PAY-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8
- Data Requirements: S7 (tenant_id discriminator + RLS policy)

## 3. Preconditions
- Tenant "acme" has employee Ravi (Tenant A) with an FT-IN assignment and revision history.
- Tenant "globex" has employee Lena (Tenant B) with her own assignment.
- An HR Officer with `Payroll.*.All` is authenticated in globex (Tenant B).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Ravi + assignment + revisions |
| Tenant B | globex | Lena + assignment |
| Auth context | globex | HR authenticated in Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex `tenant_id`. | Tenant context resolves to globex. |
| 2 | `GET /api/v1/payroll/employees/{ravi_id}/salary` using Ravi's (acme) employee UUID. | 404 Not Found (query filter excludes acme rows); never 200 with Tenant A salary data. |
| 3 | `GET /api/v1/payroll/employees/{ravi_id}/salary/revisions`. | 404 Not Found; no acme revision history exposed. |
| 4 | Verify at the database level. | `SELECT * FROM employee_salary_component WHERE tenant_id = globex_id` returns only globex rows; Ravi's rows excluded. (If an RLS policy exists, a session set to globex cannot read acme rows even via direct query.) |
| 5 | Switch to acme context; fetch Ravi's salary. | acme HR sees Ravi's assignment + revisions; zero globex employee salary data. |

## 6. Postconditions
- No cross-tenant employee salary assignment or revision data exposed via API or query.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
