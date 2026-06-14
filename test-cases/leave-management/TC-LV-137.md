---
id: TC-LV-137
user_story: US-LV-007
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-137: Dual view -- color-coded calendar (month/year) and list view of holidays (AC-4)

## 1. Test Objective
Verify the Holiday Calendar page offers both a visual calendar view (month/year grid with holiday markers, color-coded by type) and a list view, toggled via a segmented control, both populated from the same year-scoped holiday data (AC-4, §8).

## 2. Related Requirements
- User Story: US-LV-007
- Acceptance Criteria: AC-4
- UI/UX Notes (Section 8)

## 3. Preconditions
- Tenant "acme" with several 2026 holidays of each type: Public, Restricted, Optional.
- HR Officer "Priya" authenticated with `Holiday.View`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Public color | blue | §8 |
| Restricted color | orange | §8 |
| Optional color | green | §8 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Holiday Calendar page for 2026 in calendar view | A month/year grid renders with a marker on each holiday date; markers are color-coded (Public=blue, Restricted=orange, Optional=green). |
| 2 | Toggle to list view via the segmented control | A list/table of the same holidays appears (name, date, type, location), reflecting identical data. |
| 3 | Navigate to the next/previous year using the year picker/arrows | The view reloads that year's holidays via `GET /api/v1/holidays?year=...`. |
| 4 | Verify a date with no holiday | The calendar shows no marker; the list omits it -- no phantom entries. |

## 6. Postconditions
- Both calendar and list views render the same year-scoped, color-coded holiday data.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
