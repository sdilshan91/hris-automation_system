---
id: TC-PAY-ISO-033
user_story: US-PAY-009
module: Payroll
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-PAY-ISO-033: Reports for Tenant A contain ZERO Tenant B data -- cross-tenant read isolation across every report, bank advice, tax statement, export, and dashboard (AC-5, FR-8)

## 1. Test Objective
Verify AC-5 / FR-8: every report query is scoped by tenant_id at the query level so a report generated for Tenant A includes no data from Tenant B. This covers the Payroll Summary, Register, Department Summary, Statutory, Variance, CTC reports, the Bank Advice file, Year-End Tax Statements, every export (CSV/Excel/PDF), and the analytics dashboard / pre-aggregated table. Tenant A and Tenant B run with identical periods, departments, and even identical employee names to prove no leakage.

## 2. Related Requirements
- User Story: US-PAY-009
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-2, FR-5, FR-8

## 3. Preconditions
- Tenant A "acme" + Tenant B "globex", each with a Finalized May 2026 run, overlapping department names, and at least one identically-named employee.
- HR users in each tenant with `Payroll.*.All`.
- NOTE: AC-5/FR-8 specify "RLS ensures no cross-tenant data"; this platform enforces isolation via EF Core global query filters + TenantInterceptor on the payroll tables and the pre-aggregated dashboard table -- this TC describes the EF mechanism and notes Postgres RLS as an extension point.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Period | May 2026 in BOTH tenants | overlap |
| Shared name | "John Smith" in A and B | name-collision probe |
| Surfaces | summary/register/statutory/variance/CTC/bank-advice/tax-stmt/export/dashboard | full coverage |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, generate the Payroll Summary + Register for May 2026. | Totals, employee_count, and department rows reflect ONLY acme; globex's identically-named "John Smith" and globex departments are absent (AC-5, FR-8). |
| 2 | As acme HR, generate the Bank Advice file + Year-End Tax Statements. | The bank advice rows + tax-statement set contain only acme employees; no globex account numbers or statements (FR-8). |
| 3 | Export each report (CSV/Excel/PDF) as acme. | Every exported file contains only acme rows; no globex leakage in any format (FR-2, FR-8). |
| 4 | Load the analytics dashboard as acme. | Charts/pre-aggregated figures derive only from acme's `pay_month/pay_year/department_id` rows filtered by tenant_id; globex aggregates excluded (FR-5, FR-8). |
| 5 | Repeat steps 1-4 as globex HR. | Symmetric -- globex reports contain only globex data; acme excluded everywhere. |
| 6 | Inspect the generated SQL/query for a report. | Each report query carries the `tenant_id == current tenant` predicate (EF global filter / TenantInterceptor); no query omits the filter (FR-8). |

## 6. Postconditions
- All report/bank-advice/tax-statement/export/dashboard surfaces are tenant-filtered at the query level; no cross-tenant data leaks in any tenant's reports.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
