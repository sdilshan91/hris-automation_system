---
id: TC-ATT-080
user_story: US-ATT-006
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-080: Overtime calculation is deterministic and auditable -- the formula and inputs are logged (security/audit)

## 1. Test Objective
Verify NFR-3: overtime detection is deterministic (same inputs -> same overtime_minutes/multiplier every time) and auditable (the exact formula and inputs -- standard_hours, threshold, net work minutes, day type, applicable caps -- are recorded so a decision can be reconstructed).

## 2. Related Requirements
- User Story: US-ATT-006
- Non-Functional: NFR-3 (deterministic + auditable; formula and inputs logged)
- Functional Requirements: FR-1 (detection condition), FR-3 (multiplier), FR-8 (caps)

## 3. Preconditions
- Tenant "acme" with known overtime rules (standard 480, threshold 30, weekday 1.5x, daily cap 240, weekly cap 1200).
- A reproducible clock-in/out producing a known net total (e.g. 540 min).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| net_work_minutes | 540 | input |
| standard_hours | 480 | input |
| threshold | 30 | input |
| day type | weekday | input |
| expected overtime_minutes | deterministic | same on every run |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run the same clock-out scenario multiple times (or recompute via the domain calculator) | Identical overtime_minutes and multiplier each time -- no nondeterminism (no wall-clock-dependent rounding, stable truncation). |
| 2 | Inspect the audit/log for the detection | The record/audit captures the inputs (net minutes, standard, threshold, day type, applicable caps) and the resulting overtime_minutes + multiplier, enough to reconstruct the calculation. |
| 3 | Confirm the calculation is a pure helper | Detection uses a deterministic domain calculator (analogue of `AttendanceCalculator`), truncating partial minutes consistently and never producing negative overtime. |
| 4 | Change one input (e.g. threshold 30 -> 15) and recompute | The output changes predictably and the new inputs are recorded -- the log reflects the actual config used at detection time. |
| 5 | Cap interaction | When a cap applies (TC-ATT-070), the audit records both the raw computed overtime and the capped value so the truncation is explainable. |

## 6. Postconditions
- Overtime detection is reproducible and its inputs/formula are auditable for every record.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The attendance vault documents `AttendanceCalculator` as a pure, minute-accurate domain helper shared by the service and the auto-clock-out job; overtime detection should reuse the same deterministic style. The exact audit payload shape should be confirmed against the backend; this TC asserts that inputs+formula+result are reconstructable, not a specific log schema.
