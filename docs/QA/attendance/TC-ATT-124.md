---
id: TC-ATT-124
user_story: US-ATT-009
module: Attendance
priority: high
type: functional
status: pass
created: 2026-06-15
---

# TC-ATT-124: Reconciliation view -- attendance summary side-by-side with payroll inputs, mismatches highlighted (attendance side rendered; payroll-input column DEFERRED)

## 1. Test Objective
Verify the reconciliation view (FR-5): `GET /api/v1/attendance/reconciliation?month=` returns, per employee, the attendance summary alongside the payroll inputs, with any discrepancy between the two highlighted. The ATTENDANCE side (the source figures and the mismatch-detection contract) is verified now; the PAYROLL-input column is populated by the Payroll module and is DEFERRED until it is built -- the view renders the attendance side and the mismatch-flagging seam.

## 2. Related Requirements
- User Story: US-ATT-009
- Functional Requirements: FR-5 (reconciliation view: attendance data side-by-side with payroll inputs, highlight discrepancies)
- UI/UX: §8 (side-by-side Notion-style table, highlighted mismatch cells; stacks on mobile)
- API: GET /api/v1/attendance/reconciliation?month=

## 3. Preconditions
- Tenant "acme"; monthly summary generated for 2026-05.
- Employees with known attendance figures (present/absent/lop/overtime).
- A modelled payroll-input set for the same employees (so a mismatch can be induced for the highlight check).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-05 | reconciliation period |
| matched employee | Asha | attendance == payroll inputs |
| mismatched employee | Ben | attendance lop_days=2 vs payroll-input lop_days=1 (induced) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /reconciliation?month=2026-05` | 200; one row per employee with the ATTENDANCE summary side populated (present/absent/lop_days/approved_overtime_minutes/work_minutes) matching the generated summary. |
| 2 | For the matched employee (Asha) | No discrepancy flagged -- attendance and payroll-input figures agree (when the payroll side is present). |
| 3 | For the mismatched employee (Ben), where payroll-input lop_days differs from attendance lop_days | The differing cell(s) are flagged/highlighted as a discrepancy (FR-5) -- the mismatch-detection contract is exercised. |
| 4 | Render the view in the UI | Side-by-side Notion-style table: attendance summary on the left, payroll inputs on the right; mismatch cells visually highlighted (§8). |
| 5 | Mobile / 360px width | The two sides STACK vertically (attendance summary on top, payroll inputs below) per §8 (a11y detail in TC-ATT-128). |
| 6 | Payroll module NOT present | The attendance side renders fully; the payroll-input column shows a clear "pending payroll" / empty state rather than erroring (DEFERRED behaviour). |

## 6. Postconditions
- The reconciliation view renders the attendance summary per employee and flags discrepancies against payroll inputs; with no Payroll module the attendance side still renders and the payroll column is a clean deferred state. No mutation.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

## 8. Notes
- **Payroll-input column is PAYROLL-MODULE and DEFERRED** -- the right-hand side of the reconciliation table is populated by the Payroll engine's computed inputs, which do not exist yet. The attendance source figures and the mismatch-highlight CONTRACT are verified now; the live cross-system comparison is exercised when Payroll lands. Consistent with the deferred payroll-consumption pattern across the module. **Reported to caller.**
- Reconciliation performance (load < 3s P95, NFR-5) is covered in TC-ATT-126; accessibility/responsive details (side-by-side table, mobile stack) in TC-ATT-128; tenant isolation in TC-ATT-ISO-012.
