---
id: TC-ATT-054
user_story: US-ATT-005
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-054: FLEXIBLE shift requires only minimum_hours; start_time/end_time are optional and not validated (BR-8, FR-1)

## 1. Test Objective
Verify the FLEXIBLE shift type (FR-1, BR-8): a FLEXIBLE shift is created with only `minimum_hours` enforced; `start_time` and `end_time` may be null (the zero-duration BR-7 rule and start/end consistency checks do NOT apply to FLEXIBLE shifts). Only `minimum_hours` is validated (required, positive, fits decimal(4,2)).

## 2. Related Requirements
- User Story: US-ATT-005
- Functional Requirements: FR-1 (FLEXIBLE type), FR-2 (minimum_hours parameter)
- Business Rules: BR-8 (FLEXIBLE: only minimum_hours enforced; start/end not validated)

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled.
- HR Officer authenticated with `Attendance.Shift.Manage`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| name | "Flex 8h" | |
| type | FLEXIBLE | |
| start_time / end_time | null / null | Allowed for FLEXIBLE |
| minimum_hours | 8.00 | Enforced |
| working_days | [1,2,3,4,5] | Applicable days still apply (BR-6) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST .../shifts` with type FLEXIBLE, start_time/end_time null, minimum_hours 8.00 | Response 201; shift created with type FLEXIBLE, null start/end, minimum_hours 8.00. (BR-7 zero-duration does NOT fire because there are no fixed times.) |
| 2 | `POST .../shifts` FLEXIBLE with minimum_hours omitted/null | Response 400; minimum_hours is required for FLEXIBLE. |
| 3 | `POST .../shifts` FLEXIBLE with minimum_hours = 0 or negative | Response 400; minimum_hours must be positive. |
| 4 | `POST .../shifts` FLEXIBLE with minimum_hours = 999.99 (decimal(4,2) max) accepted; 1000.00 rejected | Boundary: the decimal(4,2) range is enforced. |
| 5 | Confirm start/end are not validated for FLEXIBLE | Supplying a start_time without end_time (or vice versa) on a FLEXIBLE shift does NOT raise the SINGLE start/end consistency errors; only minimum_hours governs validity (BR-8). |

## 6. Postconditions
- A FLEXIBLE shift exists with only minimum_hours enforced; no fixed start/end required.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
