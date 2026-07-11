---
id: TC-PAY-ISO-013
user_story: US-PAY-004
module: Payroll
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-PAY-ISO-013: Tenant B cannot access Tenant A's payslip storage path or download Tenant A's PDFs (cross-tenant read isolation)

## 1. Test Objective
Verify AC-4: payslip blob storage is tenant-scoped and the download/preview APIs enforce isolation, so a user authenticated in Tenant B can NEVER read, preview, download (single or bulk ZIP), or enumerate Tenant A's payslip PDFs -- not via the API, not via guessing the `{tenantA}/payroll/{runId}/{employeeId}.pdf` blob path. Reads are filtered by EF Core global query filters on `payroll_slip`/`payroll_run`; the blob path is derived server-side from the resolved tenant, never from client input. (US-PAY-004 AC-4 says "API enforces RLS"; this platform enforces via EF query filters + a tenant-derived blob prefix -- if Postgres RLS policies are later added on the payroll tables, extend Step 5 to assert session-level isolation as defense-in-depth.)

## 2. Related Requirements
- User Story: US-PAY-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5 (tenant-scoped path), FR-6 (download endpoints)
- Data Requirements: S7 (tenant_id on payroll_run / payroll_slip)

## 3. Preconditions
- Tenant "acme" (A): run Ra with generated payslips at `{acmeTenantId}/payroll/{Ra}/{EMP}.pdf`.
- Tenant "globex" (B): its own user "Bob" with `Payroll.*.All` in globex.
- Bob knows (or guesses) acme's runId Ra and an acme employeeId.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | owns payslips |
| Tenant B | globex | attacker context |
| Target | Ra + acme employeeId | A's payslip |
| A's blob prefix | {acmeTenantId}/payroll/{Ra}/ | tenant-scoped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Bob (globex, `X-Tenant-Subdomain: globex`), GET single-download for acme's runId Ra + acme employeeId. | 404 Not Found (run/slip filtered out of globex's scope); no PDF bytes returned; not 403-with-leak vs 404 distinction does not reveal existence. |
| 2 | As Bob, POST Download-All for acme's runId Ra. | 404; no ZIP produced; no acme PDFs enumerated. |
| 3 | As Bob, GET the inline preview for an acme slip. | 404; nothing rendered. |
| 4 | As Bob, list payslips for runId Ra. | Empty/404 -- globex cannot see acme's run or its slips. |
| 5 | Direct DB cross-check: query `payroll_slip` for Ra while tenant context = globex. | EF global query filter returns zero rows (the slips' tenant_id=acme != globex). (If RLS is added, a globex DB session also returns zero.) |
| 6 | Verify Bob's own globex payslips are still fully accessible. | globex downloads/previews work normally -- isolation blocks only cross-tenant access. |

## 6. Postconditions
- Tenant B can neither read, download, nor enumerate Tenant A's payslip PDFs by any path; blob access is tenant-confined.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
