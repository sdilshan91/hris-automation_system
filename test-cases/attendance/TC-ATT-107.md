---
id: TC-ATT-107
user_story: US-ATT-008
module: Attendance
priority: critical
type: functional
status: pass
created: 2026-06-14
---

# TC-ATT-107: Late deduction rule -- 3 lates in a month = 0.5-day deduction flagged in the monthly summary, feeding LOP (AC-4, FR-4, BR-4)

## 1. Test Objective
Verify AC-4/FR-4/BR-4: when a tenant configures a late-deduction policy (e.g. 3 lates = 0.5-day deduction) and an employee accumulates the threshold number of late arrivals within the policy period, the system flags the applicable deduction in the monthly summary, which feeds LOP for payroll. The attendance-side deduction flag is verified now; payroll CONSUMPTION is owned by US-ATT-009/Payroll (CONDITIONAL).

## 2. Related Requirements
- User Story: US-ATT-008
- Acceptance Criteria: AC-4
- Functional Requirements: FR-4 (tenant-configurable deduction rules)
- Business Rules: BR-4 (3 lates = 0.5 day; 6 lates = 1 day; deductions feed LOP)
- Dependency: US-ATT-007 (monthly summary aggregation), US-ATT-009 (payroll LOP consumption)

## 3. Preconditions
- Tenant "acme"; a late_policy configured: threshold_count = 3, deduction_days = 0.5, period = MONTHLY, is_active = true.
- Employee "Asha" on a 09:00 SINGLE shift, 15-min grace.
- The monthly summary computation (US-ATT-007) is available for the current month.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| threshold_count | 3 | lates triggering deduction |
| deduction_days | 0.5 | per threshold reached |
| period | MONTHLY | evaluation window |
| late arrivals seeded | 3 (e.g. on the 3rd/10th/17th, each clock-in past grace) | reaches threshold |
| expected deduction flag | 0.5 day | surfaced in monthly summary |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Seed 2 late arrivals for Asha in the month, then read the monthly summary | Late count = 2; NO deduction flagged yet (below threshold). |
| 2 | Record the 3rd late arrival (clock-in past grace) | The 3rd late is flagged is_late on its attendance_log (TC-ATT-101 mechanism). |
| 3 | Read Asha's monthly summary / `GET /api/v1/attendance/late-early/my-score?month=` | Late count = 3; a 0.5-day late-deduction is flagged for the month (AC-4) and surfaced as a deduction/LOP-feeding value (BR-4). |
| 4 | Verify the deduction feeds LOP | The 0.5-day deduction is exposed to the monthly summary's LOP-feeding value (US-ATT-007 BR-3 lop_days); CONSUMPTION into a payroll deduction is CONDITIONAL on US-ATT-009/Payroll (the attendance-side flag is set now). |
| 5 | Verify employee notification | Reaching the deduction threshold notifies the employee (AC-4) -- dispatch SEAM (recipient = Asha, payload references the late count + deduction) verified now; in-app delivery DEFERRED on US-NTF (see TC-ATT-109). |
| 6 | Verify the 6-lates tier (BR-4) | If a 6-lates = 1-day tier is configured, accumulating 6 lates flags a 1-day deduction (the policy supports multiple/escalating tiers). |

## 6. Postconditions
- Asha's monthly summary carries a 0.5-day late-deduction flag for the month, tenant-scoped; available to feed payroll LOP (CONDITIONAL on US-ATT-009).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Payroll consumption (BR-4) CONDITIONAL on US-ATT-009/Payroll.** The attendance side computes and flags the deduction (0.5/1 day) now; the actual salary deduction is applied by the Payroll engine -- consistent with US-ATT-007 TC-ATT-089 (lop_days) and US-ATT-006 TC-ATT-074 (payroll-ready). **Reported to caller.**
- **Threshold tiering ambiguity:** BR-4 lists "3 lates = 0.5 day; 6 lates = 1 day." Whether the policy stores a single (threshold_count, deduction_days) pair or a tiered table is a data-model question -- the late_policy schema (S7) shows a single pair. This TC asserts the single-tier 0.5-day case (Step 1-5) and treats the 6-lates tier (Step 6) as CONDITIONAL on multi-tier support. **Reported to caller.**
