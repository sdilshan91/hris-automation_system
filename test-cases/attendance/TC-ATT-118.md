---
id: TC-ATT-118
user_story: US-ATT-009
module: Attendance
priority: critical
type: integration
status: draft
created: 2026-06-15
---

# TC-ATT-118: Payroll data pull -- monthly summary generated -> payroll-data API returns per-employee present/absent/lop/approved-overtime/work-minutes/late-deduction inputs

## 1. Test Objective
Verify the attendance-to-payroll API (AC-1, FR-1, FR-2): once the monthly attendance summary (US-ATT-007) is generated for the period, `GET /api/v1/attendance/payroll-data?month=&employeeIds=` returns, per employee, the salary-calculation inputs -- `total_working_days`, `total_present_days`, `total_absent_days`, `lop_days`, `late_deduction_days`, `approved_overtime_minutes`, `overtime_multiplier_details`, `total_work_minutes` -- scoped to the requesting tenant and the supplied employee list. This is the attendance SOURCE side of the integration; the payroll engine's salary computation is the Payroll module's responsibility (see TC-ATT-121).

## 2. Related Requirements
- User Story: US-ATT-009
- Acceptance Criteria: AC-1 (system pulls present/LOP/approved-overtime into payroll inputs when payroll run is initiated)
- Functional Requirements: FR-1 (API endpoint payroll calls for tenant/period/employee list), FR-2 (per-employee fields returned)
- Data: §7 attendance-to-payroll API response (per employee)
- API: GET /api/v1/attendance/payroll-data?month=YYYY-MM&employeeIds=

## 3. Preconditions
- Tenant "acme"; Attendance and Payroll modules enabled (§2).
- Monthly attendance summary generated for 2026-05 (US-ATT-007) for the requested employees.
- Payroll period 2026-05 not yet finalized.
- Employee "Asha" has a known mix for May: present days, some absences, approved overtime, and a late-deduction day (seeded so each field is non-trivial).
- Caller is authenticated as an HR Officer (HR-only authz verified in TC-ATT-127).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-05 | varchar(7) period |
| employeeIds | [Asha, Ben] | explicit employee list |
| Asha total_working_days | 22 | from shift + calendar |
| Asha present | 19.0 | incl. any half-days |
| Asha absent | 2.0 | unexcused |
| Asha approved OT | 600 min | 10h approved at 1.5x |
| Asha late_deduction_days | 0.5 | from US-ATT-008 late policy |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As HR (acme), `GET /api/v1/attendance/payroll-data?month=2026-05&employeeIds=Asha,Ben` | 200 OK; one object per requested employee, each carrying employee_id + period="2026-05". |
| 2 | Inspect Asha's row fields | All FR-2 fields present and correctly typed: `total_working_days`(22), `total_present_days`(19.0), `total_absent_days`(2.0), `lop_days`, `late_deduction_days`(0.5), `approved_overtime_minutes`(600), `overtime_multiplier_details`(jsonb breakdown), `total_work_minutes`. |
| 3 | Confirm the values match the generated monthly summary (US-ATT-007) for Asha | Payroll-data is sourced from the same computed summary -- no divergence between the summary table and the payroll-data response. |
| 4 | Omit `employeeIds` (request whole period) | Returns rows for all tenant employees with a summary for the period (bounded by tenant scope; large-set SLA in TC-ATT-126). |
| 5 | Request a `month` with NO generated summary | Empty/clear "summary not generated" contract (not a partial/garbage pull) -- payroll cannot proceed on un-generated data (precondition §2). |
| 6 | Request a future/invalid `month` (e.g. 2026-13) | 400 validation error; no data returned. |

## 6. Postconditions
- The payroll-data response mirrors the generated monthly summary for the period, scoped to tenant + requested employees. No attendance data is mutated by the read.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- LOP calculation detail (lop_days derivation) is exercised in TC-ATT-119; approved-overtime-only filtering in TC-ATT-120; the downstream salary FORMULAS (LOP deduction, overtime pay) are PAYROLL-MODULE responsibility and DEFERRED -- TC-ATT-121 verifies the attendance INPUTS are correct. **Reported to caller.**
- FR-6 (refresh payroll when attendance changes during an active run) is the payroll-run-side trigger; the attendance-side data-availability is verified here, the refresh consumption is CONDITIONAL on the Payroll module (TC-ATT-123 covers the unlock->recalc seam). **Reported to caller.**
- Tenant isolation of this endpoint is covered by TC-ATT-ISO-012.
