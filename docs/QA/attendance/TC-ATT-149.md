---
id: TC-ATT-149
user_story: US-ATT-011
module: Attendance
priority: critical
type: integration
status: automated
created: 2026-07-15
---

# TC-ATT-149: Four-tier resolution — single-branch tenant (empty Location tier) falls through to the tenant Mon–Fri default (AC-2c)

## 1. Test Objective
Verify US-ATT-011 AC-2 / BR-4: a single-branch tenant with **no Location `DefaultShiftId` configured** resolves the **tenant default shift** (Mon–Fri `{1,2,3,4,5}`) exactly as before — proving the Location tier is additive and the empty-tier fall-through is intact (backward compatibility).

## 2. Related Requirements
- User Story: US-ATT-011
- Acceptance Criteria: AC-2
- Business Rules: BR-3 (precedence), BR-4 (single-branch untouched)

## 3. Preconditions
- Tenant with a default shift `Shift.IsDefault = true` (Mon–Fri) and no Location, or a Location whose `DefaultShiftId` is null.
- Employee with no personal shift.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Location tier | empty (null `DefaultShiftId`) | forces fall-through |
| Tenant default shift | Mon–Fri `{1,2,3,4,5}` | expected result |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Resolve the working-day set for the employee. | Set == `{1,2,3,4,5}` (tenant default). |
| 2 | Assert the tier that supplied the result. | Tenant tier (not Location, not code default). |
| 3 | Remove even the tenant default (no `IsDefault` shift). | Falls through to code default Mon–Fri `{1,2,3,4,5}` — still 5 working days. |

## 6. Postconditions
- Single-branch behaviour is identical to pre-epic; no configuration required.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, real Postgres/Testcontainers):**
  - `ShiftScheduleResolverLocationTierTests.Resolver_EmptyLocationTier_FallsThroughToTenantDefault` — note the tenant default is seeded **Mon–Sat** on purpose: the code default (tier 4) is Mon–Fri, so a Mon–Fri tenant default would make tiers 3 and 4 indistinguishable. **Saturday is the discriminator.**
  - `ShiftScheduleResolverLocationTierTests.Resolver_NoShiftConfiguredAtAll_ResolvesCodeDefaultMonFri_NotEmptySet` — tier 4. Pins the US-ATT-011 behaviour change: the resolver previously returned an EMPTY set, which callers read as "every calendar day is a working day" (see BUG-287); it now returns Mon–Fri and never an empty set.
- Backing suite trait: `[Trait("TC", "TC-ATT-149")]`.
