---
id: TC-ATT-120
user_story: US-ATT-009
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-15
---

# TC-ATT-120: Only APPROVED overtime feeds payroll data -- pending/rejected overtime excluded; approved_overtime_minutes + per-rate multiplier breakdown surfaced

## 1. Test Objective
Verify that the payroll-data pull includes ONLY approved overtime (FR-8, BR-5): pending and rejected overtime records are excluded from `approved_overtime_minutes`, and the `overtime_multiplier_details` jsonb correctly breaks the approved minutes down by multiplier rate (e.g. weekday 1.5x vs weekend 2.0x). This is the attendance-side overtime INPUT for the AC-3 overtime-pay computation (which is PAYROLL-MODULE, TC-ATT-121).

## 2. Related Requirements
- User Story: US-ATT-009
- Acceptance Criteria: AC-3 (approved overtime hours feed overtime-pay inputs) -- attendance-input side
- Functional Requirements: FR-8 (overtime pay inputs use approved overtime only; pending/rejected excluded)
- Business Rules: BR-5 (only approved regularizations and approved overtime included; pending excluded)
- Data: §7 approved_overtime_minutes, overtime_multiplier_details (jsonb)
- Dependency: US-ATT-006 approved overtime records
- API: GET /api/v1/attendance/payroll-data?month=

## 3. Preconditions
- Tenant "acme"; monthly summary generated for 2026-05.
- Employee "Asha" has, for May: an APPROVED overtime record (10h @ 1.5x weekday), a PENDING overtime record (2h), and a REJECTED overtime record (1h). Optionally an approved weekend OT block (2h @ 2.0x).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Approved weekday OT | 600 min @ 1.5x | counts |
| Approved weekend OT | 120 min @ 2.0x | counts (separate rate bucket) |
| Pending OT | 120 min | excluded |
| Rejected OT | 60 min | excluded |
| Expected approved_overtime_minutes | 720 | 600 + 120 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET payroll-data?month=2026-05&employeeIds=Asha` | `approved_overtime_minutes = 720` (only the two APPROVED records); the pending 120 and rejected 60 are NOT included (FR-8, BR-5). |
| 2 | Inspect `overtime_multiplier_details` jsonb | Breakdown by rate: {1.5x: 600 min, 2.0x: 120 min} (or equivalent shape) -- so payroll can apply each multiplier; total of buckets == approved_overtime_minutes. |
| 3 | Adjust the pending record to APPROVED, regenerate/refresh, re-pull | The newly-approved minutes now appear in approved_overtime_minutes and the multiplier breakdown (approved-state drives inclusion). |
| 4 | Re-pull after the rejected record stays rejected | Rejected minutes never appear regardless of regeneration. |
| 5 | Employee with NO approved overtime | `approved_overtime_minutes = 0`; overtime_multiplier_details empty/zeroed (not null-crash). |
| 6 | Verify manager-ADJUSTED approved overtime (US-ATT-006 adjust) | The ADJUSTED (not originally-claimed) minutes are what feed payroll-data. |

## 5b. Negative / boundary
- Overtime that is approved but flagged UNAPPROVED-for-payroll (no pre-approval, US-ATT-006 BR-6) must NOT be counted -- only payroll-ready approved overtime feeds the pull.

## 6. Postconditions
- approved_overtime_minutes and overtime_multiplier_details reflect only approved (payroll-ready) overtime, partitioned by multiplier rate. No mutation by the read.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The approved/pending/rejected overtime lifecycle is owned by US-ATT-006 (TC-ATT-074 approve, TC-ATT-076 reject, TC-ATT-075 adjust, TC-ATT-072 UNAPPROVED exclusion). This TC verifies the PAYROLL-DATA pull honours that state. The payroll-ready -> consumption seam deferred in TC-ATT-072/074 is partially closed here on the attendance-output side; the salary computation remains PAYROLL-MODULE. **Reported to caller.**
- The public-holiday 2.5x rate bucket depends on the holiday-source integration (US-LV-007), CONDITIONAL as in US-ATT-006 TC-ATT-069; weekday 1.5x / weekend 2.0x buckets are verifiable now from shift working_days. **Reported to caller.**
