---
id: TC-LV-140
user_story: US-LV-007
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-140: Holiday in a finalized payroll period cannot be deleted, only deactivated (BR-4, CONDITIONAL on payroll module)

## 1. Test Objective
Verify BR-4: a holiday whose date falls within a finalized payroll period cannot be hard-deleted; it may only be deactivated. Confirm the always-available behaviour now (soft-deactivate retains the row, excludes it from calc/calendar) and mark the payroll-period-lock guard CONDITIONAL on the payroll module (period-lock entity not yet implemented).

## 2. Related Requirements
- User Story: US-LV-007
- Business Rules: BR-4
- Functional Requirements: FR-1
- Dependency: Payroll module (finalized-period lock) -- not yet implemented

## 3. Preconditions
- Tenant "acme" active; HR Officer "Priya" authenticated with `Holiday.Deactivate` / `Holiday.Edit`.
- A holiday "Year-End Holiday" on 2026-12-31 exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Holiday | "Year-End Holiday", 2026-12-31 | -- |
| Payroll period state | finalized (when payroll exists) | CONDITIONAL |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Deactivate the holiday via `POST /api/v1/holidays/{id}/deactivate` | 200 OK; row retained with `is_active=false`, excluded from leave calc and calendar views (BR-4 deactivate path -- verified now). |
| 2 | Confirm no hard-delete endpoint deletes the row destructively | Soft-deactivate / soft-delete is the only removal path; the row persists for history. |
| 3 | (CONDITIONAL -- payroll present) Attempt to delete a holiday inside a finalized payroll period | Blocked with a "finalized payroll period" error; only deactivation is allowed. Mark CONDITIONAL on the payroll period-lock. |
| 4 | Record the dependency honestly | The payroll-period-lock guard is CONDITIONAL on the payroll module; the deactivate-only retention behaviour is verified now (not a silent gap). |

## 6. Postconditions
- Holidays are retained via deactivation; the payroll-period delete-block is verified once payroll exists.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
