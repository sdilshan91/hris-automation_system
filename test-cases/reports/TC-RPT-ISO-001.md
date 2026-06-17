---
id: TC-RPT-ISO-001
user_story: US-RPT-001
module: Reports & Analytics
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-RPT-ISO-001: Same report in Tenant A and Tenant B shows only own-tenant data; no cross-tenant leakage (AC-5)

## 1. Test Objective
Verify that generating the same report type in Tenant A and Tenant B returns strictly each
tenant's own data, with no cross-tenant leakage. Directly validates AC-5 and the Test Hint
"generate the same report type in Tenant A and B; verify each shows only their own data".

## 2. Related Requirements
- User Story: US-RPT-001
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7 (tenant_id in query context)
- Non-Functional: NFR-2 (tenant isolation)
- Business Rules: BR-1

## 3. Preconditions
- Tenant A and Tenant B both active with DISTINCT, known populations:
  Tenant A = 43 active; Tenant B = 17 active; non-overlapping departments and employees.
- `hrA` (Reports.View.All in A) and `hrB` (Reports.View.All in B) authenticated in their own tenants.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A active | 43 | distinct from B |
| Tenant B active | 17 | distinct from A |
| report_type | headcount_summary | run identically in both |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, generate Headcount Summary (full tenant) | Total = 43 (Tenant A only) |
| 2 | As `hrB`, generate the identical Headcount Summary | Total = 17 (Tenant B only); none of A's 43 appear |
| 3 | Compare department/sub-department breakdowns | A shows only A's departments; B shows only B's; zero overlap |
| 4 | As `hrA`, generate Demographics and Turnover | All figures derive solely from Tenant A's population |
| 5 | As `hrB`, generate the same reports | All figures derive solely from Tenant B's population |
| 6 | Diff the two tenants' rendered employee-level/aggregate outputs | No employee, department, or location from one tenant appears in the other's report |

## 6. Postconditions
- Each tenant's reports reflect only that tenant; no cross-tenant data observed.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
