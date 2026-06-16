---
id: TC-PAY-ISO-041
user_story: US-PAY-011
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PAY-ISO-041: Cross-tenant READ isolation -- Tenant B cannot see/enumerate Tenant A's payslip_email_log rows, distribution summaries, or the run's email status; A's distribution consumes ZERO B employees/payslips (name-collision probe)

## 1. Test Objective
Verify AC-5 and FR-8: the payslip email distribution surface is tenant-scoped via EF Core global query filters + `ITenantContext`. A user in Tenant B can NEVER read, list, or enumerate Tenant A's `payslip_email_log` rows, distribution summary, or per-run email status; and a Tenant A distribution job fetches ONLY A's employees + A's payslips (zero B records), even when A and B have employees with identical names/emails (name-collision probe). (US-PAY-011 AC-5/FR-8 say tenant isolation; this platform enforces via EF query filters + TenantInterceptor -- Postgres RLS noted as an extension point.)

## 2. Related Requirements
- User Story: US-PAY-011
- Acceptance Criteria: AC-5
- Functional Requirements: FR-2, FR-5, FR-8
- Data Requirements: S7 (tenant_id on payslip_email_log, RLS-enforced)

## 3. Preconditions
- Tenant "acme" (A): Finalized run Ra; payslips distributed; `payslip_email_log` rows exist with a summary.
- Tenant "globex" (B): its own user "Bob" with `Payroll.*.All`; B has an employee with the SAME name + email as an acme employee (collision probe).
- Bob knows (or guesses) acme's runId Ra.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | owns email logs |
| Tenant B | globex | attacker context |
| Collision | same name/email in A and B | must not cross-resolve |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Bob (globex, `X-Tenant-Subdomain: globex`), GET the distribution summary / email-log list for acme's runId Ra. | 404/empty -- the run + its email logs are filtered out of globex scope; no rows, summary, or recipient addresses leaked. |
| 2 | As Bob, GET per-employee email status for an acme employeeId in Ra. | 404/empty; acme delivery status not exposed. |
| 3 | Trigger a fresh distribution for acme run Ra (as the acme HR user) and inspect which employees/payslips it fetched. | Only acme employees + acme `{acmeTenantId}/payroll/{Ra}/...` PDFs are read; ZERO globex employees/payslips consumed despite the name/email collision. |
| 4 | Direct DB cross-check: query `payslip_email_log` for Ra while tenant context = globex. | EF global query filter returns zero rows (tenant_id=acme != globex). (If RLS is later added, a globex DB session also returns zero.) |
| 5 | Confirm Bob's own globex distribution logs/summaries are fully visible to Bob. | globex sees only globex logs -- isolation blocks cross-tenant reads only. |

## 6. Postconditions
- Tenant B cannot read or enumerate Tenant A's email logs/summaries; A's distribution derives solely from A's data even under name/email collisions.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
