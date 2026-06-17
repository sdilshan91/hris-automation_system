---
id: TC-RPT-ISO-005
user_story: US-RPT-002
module: Reports & Analytics
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-RPT-ISO-005: Leave/attendance report in Tenant A vs Tenant B shows only own-tenant data; no leakage (AC-5)

## 1. Test Objective
Verify that generating the same leave/attendance report (utilization, balance, attendance, overtime,
absenteeism) in Tenant A and Tenant B returns strictly each tenant's own data, with no cross-tenant
leakage. Directly validates AC-5 and the Test Hint "generate the same report in Tenant A and B;
verify data independence".

## 2. Related Requirements
- User Story: US-RPT-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7 (tenant_id in query context)
- Non-Functional: NFR-2 (tenant isolation)
- Business Rules: BR-1

## 3. Preconditions
- Tenant A and Tenant B both active with DISTINCT, known leave/attendance data:
  Tenant A = 10 employees with known leave totals; Tenant B = 6 employees with different totals;
  non-overlapping departments, leave types, shifts, and employees.
- `hrA` (Reports.View.All in A) and `hrB` (Reports.View.All in B) authenticated in their own tenants.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A Annual total | 42 days | distinct from B |
| Tenant B Annual total | 19 days | distinct from A |
| report_type | leave_utilization / attendance_summary | run identically in both |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, generate Leave Utilization (full tenant) | Annual total = 42 (Tenant A only) |
| 2 | As `hrB`, generate the identical Leave Utilization | Annual total = 19 (Tenant B only); none of A's employees/totals appear |
| 3 | As `hrA` then `hrB`, generate Attendance Summary | Each tenant's attendance/overtime/absenteeism derives solely from its own population |
| 4 | As `hrA` then `hrB`, generate Leave Balance | Each tenant's balance rows list only its own employees |
| 5 | Compare department/leave-type/shift breakdowns | A shows only A's; B shows only B's; zero overlap |
| 6 | Diff the two tenants' rendered outputs | No employee, department, leave type, or shift from one tenant appears in the other's report |

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
