---
id: TC-ATT-158
user_story: US-ATT-005
module: Attendance
priority: high
type: functional
status: automated
created: 2026-07-19
automated: 2026-07-19
defect:
  - ISSUE-077
---

# TC-ATT-158: Set/transfer the tenant default shift keeps exactly one default (ISSUE-077 — SetDefault endpoint enforces BR-1)

## 1. Test Objective
Verify the ISSUE-077 fix on US-ATT-005: the `PUT shifts/{id}/default` endpoint (`ShiftService.SetDefaultAsync`) enforces the **BR-1 "exactly one default" invariant** when the tenant default shift is set/transferred. Setting a new default must **clear** the flag on the prior default and set it on the target, so the resolver's `IsDefault` fallback (FR-5) stays unambiguous. Re-setting the current default is an idempotent no-op that writes no audit row, and pointing at an unknown shift returns 404.

## 2. Related Requirements
- User Story: US-ATT-005
- Functional Requirement: FR-5 (default shift per tenant for employees without explicit assignments)
- Business Rule: BR-1 (every tenant must have at least one default shift; exactly one at a time)
- Finding: ISSUE-077 (PR #371)

## 3. Preconditions
- A tenant with at least one existing shift flagged `IsDefault = true` (e.g. the seeded "General Shift") and one non-default target shift.
- Uses the EF Core InMemory provider through `ShiftService` (mirrors `ShiftServiceTests`); the BR-1 invariant is enforced by the service-level flag transfer, not a partial unique index.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Old default shift | "General Shift" (`IsDefault = true`) | must be cleared on transfer |
| Target shift | "Gulf Sun-Thu" (`IsDefault = false`) | receives the default flag |
| Unknown shift id | random `Guid` | no such shift → 404 |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Call `SetDefaultAsync(targetId)` where a different shift is currently the default. | Success; the returned DTO is the target with `IsDefault = true`. The previous default is cleared → **exactly one** default remains and it is the target. | `ShiftServiceTests.SetDefault_transfers_the_flag_and_keeps_exactly_one_default` |
| 2 | Call `SetDefaultAsync(defaultId)` on the shift that is already the default. | Idempotent success; still exactly one default; **no** `Shift.DefaultSet` audit row is written (no transfer occurred). | `ShiftServiceTests.SetDefault_on_the_current_default_is_an_idempotent_noop` |
| 3 | Call `SetDefaultAsync(randomGuid)` for a shift that does not exist. | Failure with status code **404**. | `ShiftServiceTests.SetDefault_unknown_shift_is_404` |

## 6. Postconditions
- Exactly one shift carries `IsDefault = true` per tenant after any set/transfer; the resolver's default-tier fallback stays deterministic.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test (idempotent transfer / exactly-one invariant)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, EF Core InMemory through the real service):**
  - `ShiftServiceTests.SetDefault_transfers_the_flag_and_keeps_exactly_one_default`
  - `ShiftServiceTests.SetDefault_on_the_current_default_is_an_idempotent_noop`
  - `ShiftServiceTests.SetDefault_unknown_shift_is_404`
- Backing suite trait: `[Trait("TC", "TC-ATT-158")]`.
