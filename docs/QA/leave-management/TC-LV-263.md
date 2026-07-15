---
id: TC-LV-263
user_story: US-LV-003
module: Leave Management
priority: high
type: integration
status: draft
created: 2026-07-15
defect:
  - BUG-284
---

# TC-LV-263: Single-branch Mon–Fri leave day-count is unchanged by the resolver-driven work-week (BUG-284 control / regression)

## 1. Test Objective
Verify the BUG-284 fix does not regress the common case: a single-branch tenant on the standard Mon–Fri work-week still counts leave days, half-days, and balance preview exactly as before after routing through the `ShiftScheduleResolver`. Saturday/Sunday are excluded; a Mon–Fri span deducts the expected working-day count.

## 2. Related Requirements
- User Story: US-LV-003
- Acceptance Criteria: leave day-count against the work-week
- Defect: BUG-284 (control arm — guards against over-correction)

## 3. Preconditions
- A single-branch tenant employee resolving the tenant/code default Mon–Fri `{1,2,3,4,5}` (no Location shift).
- A leave type with sufficient balance.
- Postgres-backed context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Work-week | `{1,2,3,4,5}` (Mon–Fri) | tenant/code default |
| Leave span | Fri → next Mon | crosses a weekend |
| Expected deduction | 2 workdays | Fri + Mon (Sat/Sun excluded) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Request leave Friday through the following Monday. | Deducts **2** working days (Sat/Sun excluded). |
| 2 | Request a half-day on Saturday. | Rejected (weekend). |
| 3 | Request a half-day on Wednesday. | Accepted (working day). |
| 4 | Compare the deduction to the pre-fix Mon–Fri result. | Identical — no regression for single-branch tenants. |

## 6. Postconditions
- Mon–Fri leave counting is preserved; the fix is additive.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
