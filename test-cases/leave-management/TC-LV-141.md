---
id: TC-LV-141
user_story: US-LV-007
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-141: Recurring holiday auto-generates next year's entry via Hangfire job, idempotently (FR-3, BR-5, Test Hint)

## 1. Test Objective
Verify that a holiday marked `isRecurring=true` causes the HolidayRecurrenceJob to create the equivalent next-year entry when triggered, and that re-running the job does not create duplicates (idempotent). The job runs ~30 days before year-end (1 December) per BR-5, iterating active/trial tenants (FR-3, BR-5, Test Hint §11).

## 2. Related Requirements
- User Story: US-LV-007
- Functional Requirements: FR-3
- Business Rules: BR-5
- Dependency: Hangfire recurring job (HolidayRecurrenceJob)

## 3. Preconditions
- Tenant "acme" active; HR Officer "Priya" authenticated with `Holiday.Create`.
- A recurring public holiday "Republic Day" on 2026-01-26 with `isRecurring=true`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Source holiday | "Republic Day", 2026-01-26, Public, isRecurring=true | -- |
| Target year | 2027 | next year |
| Expected new date | 2027-01-26 | same month/day |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Trigger HolidayRecurrenceJob for target year 2027 | A new holiday "Republic Day" is created on 2027-01-26 for tenant "acme" (same name/type/location, also recurring). |
| 2 | GET `/api/v1/holidays?year=2027` | The generated 2027-01-26 entry is listed. |
| 3 | Re-run the job for 2027 | No duplicate is created -- the job skips holidays whose next-year entry already exists (idempotent). |
| 4 | Verify non-recurring holidays are untouched | A holiday with `isRecurring=false` does NOT get a next-year entry. |

## 6. Postconditions
- Recurring holidays propagate to the next year exactly once; non-recurring ones do not.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
