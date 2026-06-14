---
id: TC-ATT-030
user_story: US-ATT-003
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-030: Input validation -- reason < 10 chars, future date, and clock-in not before clock-out are each rejected (negative)

## 1. Test Objective
Verify the regularization form/API enforces the field-level validation rules: (a) the reason field is mandatory and must be at least 10 characters (BR-7); (b) future dates are rejected (BR-4); and (c) when both times are provided, the requested clock-in must be before the requested clock-out, within a single calendar day, and not in the future (FR-5). Each invalid input is rejected with a clear, field-specific message and creates no `attendance_regularization` row.

## 2. Related Requirements
- User Story: US-ATT-003
- Functional Requirements: FR-5
- Business Rules: BR-4, BR-7

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, regularization workflow configured, lookback = 7 days.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Regularize.Self`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Short reason | "forgot" (6 chars) | Below the 10-char minimum (BR-7) |
| Empty reason | "" | Mandatory field missing (BR-7) |
| Future date | today + 1 day | Rejected (BR-4) |
| Inverted times | clock_in 18:00, clock_out 09:00 | clock-in not before clock-out (FR-5) |
| Cross-day times | clock_in 23:00, clock_out 02:00 next day | Spans two calendar days (FR-5) |
| Future time | clock_out in the future for today's date | Time in the future (FR-5) |
| Valid baseline date | today - 2 days | Within lookback, used for time-only checks |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Enter a 6-character reason and attempt submit | Inline validation rejects it; the live character count highlights below-minimum; API (if forced) returns 422 with a reason-length error. No row created. |
| 2 | Clear the reason entirely and attempt submit | Rejected as a mandatory-field error (BR-7). No row created. |
| 3 | Select a future date (today + 1) and submit | Rejected (BR-4) with a "cannot be a future date" message; the date picker also disables future dates. No row created. |
| 4 | For a valid past date, enter clock-in 18:00 and clock-out 09:00 and submit | Rejected (FR-5): clock-in must be before clock-out. No row created. |
| 5 | Enter clock-in 23:00 and clock-out 02:00 (next day) and submit | Rejected (FR-5): times must be within a single calendar day. No row created. |
| 6 | Enter a clock-out time later than the current moment for today's regularization and submit | Rejected (FR-5): a requested time cannot be in the future. No row created. |
| 7 | Verify the database after all attempts | No `attendance_regularization` rows were created for any of the invalid attempts. |

## 6. Postconditions
- All invalid inputs are rejected with field-specific messages; no regularization rows persisted.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
