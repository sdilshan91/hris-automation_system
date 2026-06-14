---
id: TC-LV-132
user_story: US-LV-007
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-132: Only Public holidays are excluded from leave counts; restricted/optional are not auto-excluded (AC-2, BR-2, BR-3)

## 1. Test Objective
Verify the holiday-exclusion seam excludes ONLY holidays of type Public. Restricted and optional holidays on a leave-spanned date do NOT reduce the leave day count automatically (BR-2, BR-3), matching the DB-backed `IHolidayProvider` which filters on `Type == Public`.

## 2. Related Requirements
- User Story: US-LV-007
- Acceptance Criteria: AC-2
- Business Rules: BR-2, BR-3
- Functional Requirements: FR-6

## 3. Preconditions
- Tenant "acme" active; employee "Sam" (Leave.Apply) with sufficient Annual Leave balance.
- 5-day work week; one weekday each set up as Public, Restricted, and Optional holiday.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Public holiday | 2026-07-08 (Wed) | excluded |
| Restricted holiday | 2026-07-15 (Wed) | NOT auto-excluded |
| Optional holiday | 2026-07-22 (Wed) | NOT auto-excluded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Apply for Mon-Fri of the Public-holiday week (2026-07-06..07-10) | `totalDays` = 4 (Public Wed excluded). |
| 2 | Apply for Mon-Fri of the Restricted-holiday week (2026-07-13..07-17) | `totalDays` = 5 (Restricted Wed NOT auto-excluded). |
| 3 | Apply for Mon-Fri of the Optional-holiday week (2026-07-20..07-24) | `totalDays` = 5 (Optional Wed NOT auto-excluded). |
| 4 | Confirm provider behaviour | `GET /api/v1/holidays?from&to` used by the leave calc returns only Public dates for exclusion; restricted/optional surface in the calendar view but do not drop a leave day. |

## 6. Postconditions
- Leave-day exclusion is type-sensitive: Public reduces the count; Restricted/Optional do not.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
