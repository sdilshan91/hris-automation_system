---
id: TC-ATT-090
user_story: US-ATT-007
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-090: Half-day counting -- 4 hours on an 8-hour shift counts as 0.5 present day when tenant policy supports half-day (BR-5)

## 1. Test Objective
Verify BR-5: half-day attendance -- total hours less than the standard but above 50% of the standard -- is counted as 0.5 present days when the tenant's policy supports half-day; total_present_days (decimal(4,1) per §7) reflects the fractional value. When the tenant does NOT support half-day, the same day is classified per the present/short-day rule instead.

## 2. Related Requirements
- User Story: US-ATT-007
- Functional Requirements: FR-3 (total_present_days as decimal supporting half-days)
- Business Rules: BR-1 (present-day definition), BR-5 (half-day = 0.5 if tenant supports it)
- Data Requirements: §7 total_present_days decimal(4,1)

## 3. Preconditions
- Tenant "acme" with half-day policy ENABLED; tenant "globex" with half-day policy DISABLED (for the policy-off control).
- HR Officer authenticated with `Attendance.Read.All`. Standard shift = 8h.
- Employee "Fiona" (acme): one day with 4h worked (>50% of 8h, below standard) and the rest full present days.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| standard shift | 8h | from US-ATT-005 |
| Fiona half-day | 4h worked | >50% and <100% |
| half-day policy | acme ON / globex OFF | tenant policy |
| expected (acme) | 0.5 present for that day | BR-5 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Generate the acme summary for Fiona's month | The 4h day contributes 0.5 to total_present_days; the decimal column shows the fractional total (e.g. 19.5). |
| 2 | Verify the day is not counted as a full absent or full present | It is half-present (0.5), not 1.0 present and not 1.0 absent. |
| 3 | Boundary -- exactly 50% (4h on 8h) | Classified per the BR-5 ">50%" rule: 4h00m is at the boundary; assert against the implemented inclusive/exclusive boundary and flag the exact threshold semantics. |
| 4 | Boundary -- just below 50% (e.g. 3h30m) | Not a half-day; classified short-day/absent per the tenant rule (below the half-day floor). |
| 5 | Same scenario for globex (half-day policy OFF) | The 4h day is NOT counted as 0.5; it follows the present/short-day classification for a half-day-disabled tenant. |

## 6. Postconditions
- With half-day policy enabled, a sub-standard-but->50% day counts as 0.5 present in the decimal total; with policy disabled, the half-day rule does not apply.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The exact ">50%" boundary inclusivity (is exactly 4h00m on an 8h shift a half-day or a short-day?) is a story ambiguity -- assert against the backend rule and **report to caller** if unspecified.
- The standard/minimum-hours come from the employee's shift (US-ATT-005); seeded shift config is used here.
