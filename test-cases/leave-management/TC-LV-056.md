---
id: TC-LV-056
user_story: US-LV-003
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-056: Public holidays and weekends are excluded from the leave day count

## 1. Test Objective
Verify that when an Employee applies for leave over a range that includes a public holiday and/or weekend days, those days are excluded from `total_days` and the employee is informed of the adjusted count. (Test Hint: apply for Mon-Fri where Wednesday is a holiday; verify total_days = 4.)

## 2. Related Requirements
- User Story: US-LV-003
- Acceptance Criteria: AC-6
- Functional Requirements: FR-3
- Dependency: US-LV-007 (Holiday Calendar)

## 3. Preconditions
- Tenant "acme" is active; Employee "Jane Smith" is authenticated with `Leave.Apply`.
- The tenant work week is configured as 5-day (Mon-Fri).
- A public holiday is configured on Wednesday 2026-07-08 in the holiday calendar (US-LV-007).
- Jane has sufficient Annual Leave balance.

> **Dependency note:** Holiday exclusion depends on the holiday calendar feature (US-LV-007). If US-LV-007 is not yet implemented at execution time, this test is **BLOCKED on US-LV-007** for the holiday-exclusion expectation (Steps 3, 5). The weekend-exclusion behavior (Steps 6-7), which depends only on tenant work-week config, must still pass independently.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Work week | 5-day (Mon-Fri) | Tenant config |
| Holiday | 2026-07-08 (Wed) | Public holiday (US-LV-007) |
| Range A | 2026-07-06 (Mon) to 2026-07-10 (Fri) | 5 calendar weekdays, 1 holiday |
| Expected total_days A | 4.0 | 5 weekdays minus 1 holiday |
| Range B | 2026-07-10 (Fri) to 2026-07-13 (Mon) | Spans a weekend |
| Expected total_days B | 2.0 | Fri + Mon; Sat/Sun excluded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Leave Application page; select Annual Leave | Balance panel populates. |
| 2 | Select Start = 2026-07-06 (Mon), End = 2026-07-10 (Fri) | The date picker visually marks 2026-07-08 (Wed) as a holiday (gray block). |
| 3 | Observe the "days calculated" chip | Chip shows 4.0 days (the holiday Wednesday is excluded); a note informs the user of the adjusted count. **[BLOCKED on US-LV-007 if holiday calendar absent]** |
| 4 | Submit Range A via `POST /api/v1/leaves` | Response 201 Created with `total_days: 4.00`. **[Holiday exclusion BLOCKED on US-LV-007 if absent]** |
| 5 | Verify the `leave_request` row | `total_days = 4.00`. |
| 6 | Select Range B: Start = 2026-07-10 (Fri), End = 2026-07-13 (Mon) | The chip shows 2.0 days; Sat 2026-07-11 and Sun 2026-07-12 are excluded (weekend exclusion depends only on work-week config, not US-LV-007). |
| 7 | Submit Range B | Response 201 Created with `total_days: 2.00`. |

## 6. Postconditions
- `total_days` excludes weekends per the tenant work-week config and public holidays per the holiday calendar.
- The employee is informed of the adjusted day count before submission.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
