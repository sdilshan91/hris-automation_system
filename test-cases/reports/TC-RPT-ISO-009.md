---
id: TC-RPT-ISO-009
user_story: US-RPT-003
module: Reports & Analytics
priority: critical
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: BUG-003 cross-tenant read leak CONFIRMED — isoa-admin token + X-Tenant-Subdomain:isob returns isob data (employee list ISO-2b1b-*, headcount/dashboard/leave report run under isob context). Systemic, already filed (ISSUE-193 / BUG-003 class)."
created: 2026-06-17
---

# TC-RPT-ISO-009: Payroll reports in Tenant A vs Tenant B show only own salary/payroll data; no leakage (AC-5)

## 1. Test Objective
Verify generating the same payroll report (Run Summary, Department Distribution, Statutory, Bank
Advice, CTC) in Tenant A and Tenant B returns strictly each tenant's own salary/payroll data, with no
cross-tenant leakage. Directly validates AC-5 and the Test Hint "generate payroll reports in Tenant A
and B; verify no cross-tenant data leakage". Salary figures are PII (NFR-3) — leakage is a severe
finding.

## 2. Related Requirements
- User Story: US-RPT-003
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8 (scope all payroll data by tenant_id)
- Non-Functional: NFR-2 (tenant isolation), NFR-3 (salary PII)
- Business Rules: BR-1

## 3. Preconditions
- Tenant A and Tenant B both active with DISTINCT, known finalized payroll data:
  - Tenant A 2026-03: 50 emp, gross=250,000.00, distinct departments/banks.
  - Tenant B 2026-03: 12 emp, gross=60,000.00, non-overlapping departments/employees/banks.
- `hrA` (Payroll.Export in A) and `hrB` (Payroll.Export in B) authenticated in their own tenants.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A gross | 250,000.00 / 50 emp | distinct from B |
| Tenant B gross | 60,000.00 / 12 emp | distinct from A |
| report_type | all payroll report types | run identically in both |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, generate Run Summary 2026-03 | gross=250,000.00, count=50 (Tenant A only) |
| 2 | As `hrB`, generate the identical Run Summary 2026-03 | gross=60,000.00, count=12 (Tenant B only); none of A's totals/employees appear |
| 3 | As `hrA` then `hrB`, generate Department Distribution | Each tenant's departments only; zero overlap |
| 4 | As `hrA` then `hrB`, generate Statutory + CTC | Each tenant's statutory types/employer contributions derive solely from its own population |
| 5 | As `hrA` then `hrB`, generate Bank Advice | Each tenant's employees/banks/accounts only |
| 6 | Diff the two tenants' rendered outputs | No employee, department, bank, account, or salary figure from one tenant appears in the other's report |

## 6. Postconditions
- Each tenant's payroll reports reflect only that tenant; no cross-tenant data observed.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
