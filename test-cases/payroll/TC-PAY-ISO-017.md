---
id: TC-PAY-ISO-017
user_story: US-PAY-005
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-017: An employee in Tenant B can never see, retrieve, or download Tenant A's payslips; a multi-tenant user sees only the active tenant's payslips (cross-tenant read isolation)

## 1. Test Objective
Verify AC-4 / FR-8 / NFR-4: the employee payslip read surface (list, detail, PDF) is tenant-scoped by EF Core global query filters on `payroll_slip` / `payroll_run` (plus self `employee_id` filtering). A user authenticated in Tenant B can NEVER list, read, or download Tenant A's payslips -- not via the API, not by guessing a Tenant A payslip id, not via the `{tenantA}/payroll/{runId}/{employeeId}.pdf` blob path. A user who belongs to both tenants sees only the payslips of the currently active (resolved) tenant. (US-PAY-005 FR-8 says "scoped by tenant_id via RLS"; this platform enforces via EF query filters + a tenant-derived blob prefix -- if Postgres RLS is later added on the payroll tables, extend Step 5 to assert session-level isolation as defense-in-depth.)

## 2. Related Requirements
- User Story: US-PAY-005
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5, FR-8
- Non-Functional Requirements: NFR-4
- Data Requirements: S7 (tenant_id on payroll_run / payroll_slip)

## 3. Preconditions
- Tenant A "acme": employee EMP-A01 with Finalized payslips at `{acmeTenantId}/payroll/{runA}/{EMP-A01}.pdf`.
- Tenant B "globex": its own employee EMP-B01 with `Payroll.Read.Self`.
- A multi-membership user "Dana" belongs to BOTH acme and globex (different employee records).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | owns payslips |
| Tenant B | globex | attacker context |
| Target | acme payslip id `payslipA` + runA | A's data |
| Multi-tenant user | Dana (acme + globex) | sees active tenant only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As EMP-B01 (globex, `X-Tenant-Subdomain: globex`), GET my-payslips. | Only globex (EMP-B01's) Finalized payslips returned; zero acme slips. |
| 2 | As EMP-B01, GET my-payslips/{payslipA} (acme detail id). | 404 Not Found (filtered out of globex scope); no acme data. |
| 3 | As EMP-B01, GET my-payslips/{payslipA}/pdf and attempt the guessed acme blob path. | 404; no acme PDF bytes; blob path is server-derived from the resolved tenant, never client-supplied. |
| 4 | As Dana with active tenant = globex, list payslips; then switch active tenant to acme and list. | In globex context: only Dana's globex slips. In acme context: only Dana's acme slips. Never both at once. |
| 5 | Direct DB cross-check: query `payroll_slip` for `payslipA` with tenant context = globex. | EF global query filter returns zero rows (tenant_id=acme != globex). (If RLS is added, a globex DB session also returns zero.) |
| 6 | Confirm EMP-B01's own globex payslips remain fully accessible. | globex list/detail/download work normally -- isolation blocks only cross-tenant access. |

## 6. Postconditions
- Tenant B cannot read, enumerate, or download Tenant A's payslips by any path; multi-tenant users see only the active tenant's payslips.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
