---
id: TC-LV-260
user_story: US-LV-008
module: Leave Management
priority: high
type: functional
status: automated
created: 2026-07-05
---

# TC-LV-260: Carry-forward preview renders real projected totals (not NaN) from the backend wire shape (BUG-101 regression)

## 1. Test Objective
Verify the HR carry-forward & expiry preview report renders the real projected carry-forward / forfeiture numbers from the actual backend payload. The backend serializes each row with the numeric fields `carryForward` / `forfeited` (no `projectedCarryForward` / `projectedForfeiture`, no `departmentName`); the FE must map that wire shape so the row cells and the summary strip show the true non-zero values instead of `NaN` (totals) or `0` (cells). Regression guard for BUG-101 (FE↔BE shape-drift class, same family as BUG-099/102/236).

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-5 (HR-facing preview report of projected carry-forward + forfeiture)
- Functional Requirement: FR-5
- Defect: BUG-101

## 3. Preconditions
- Carry-forward preview endpoint returns at least one row with a non-zero `carryForward` and non-zero `forfeited`.
- Automated: bound Karma/Jasmine spec drives the real `CarryForwardPreviewService` through `HttpClientTesting` (no service mock).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| carryForward | 12 | backend wire field name |
| forfeited | 3 | backend wire field name |
| projectedCarryForward / projectedForfeiture | absent | NOT emitted by the backend |
| departmentName | absent | not denormalized by the DTO |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the preview for the current year | Service issues `GET /api/v1/leaves/carry-forward-preview?year=…`. |
| 2 | Backend returns a row shaped `{ carryForward: 12, forfeited: 3, … }` (no `projected*` fields) | Service maps it to the FE row model at the boundary. |
| 3 | Inspect the desktop table row | Carry-forward cell renders `+12`; forfeited cell renders `3`. |
| 4 | Inspect the summary strip totals | Carry-forward total shows `12`, forfeited total shows `3`; the strip text never contains `NaN`. |

## 6. Postconditions
- No mutation (read-only report). The rendered figures equal the backend values.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Automation Binding
- Spec: `src/frontend/src/app/features/leave-management/components/carry-forward-preview/carry-forward-preview.component.spec.ts`
- Test: `carryForwardPreview_rendersRealTotals_notNaN_BUG101` (describe: "CarryForwardPreviewComponent — BUG-101 regression (real service, BE-shaped payload)")
