---
id: TC-LV-266
user_story: US-LV-002
module: Leave Management
priority: high
type: functional
status: automated
created: 2026-07-19
automated: 2026-07-19
defect:
  - ISSUE-034
---

# TC-LV-266: Pro-rata entitlement uses a month-fraction (denominator 12), rounds to 2dp, counts the join month as full, and is relative to the tenant's fiscal leave year (ISSUE-034 — US-LV-002 AC-4)

## 1. Test Objective
Verify the ISSUE-034 fix on US-LV-002 AC-4/FR-3/BR-2: `LeaveEntitlementEngine.CalculateProRata` computes the mid-year-joiner entitlement as a **month-fraction** (`months / 12`) rather than a day-fraction. The join month counts as a full month, the result rounds to 2 decimal places, and the month count is relative to the tenant's **fiscal** leave-year start month (not the calendar year). The service layer surfaces the same pro-rated number.

## 2. Related Requirements
- User Story: US-LV-002
- Acceptance Criteria: AC-4 (mid-year joiner gets a pro-rated entitlement)
- Functional Requirement: FR-3 (pro-rata calculation)
- Business Rule: BR-2 (pro-rata basis)
- Finding: ISSUE-034 (PR #371); interacts with ISSUE-305 fiscal leave-year

## 3. Preconditions
- `LeaveEntitlementEngine.CalculateProRata` callable directly (pure calc).
- An employee + leave type for the service path (mirrors `LeaveEntitlementServiceTests`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Jul-1 joiner, 20-day annual, calendar | → 10.00 | Jul–Dec = 6 months, 20 × 6/12 |
| Dec-31 joiner, 20-day annual | → 1.67 | join month full: Dec = 1 month, 20 × 1/12 |
| Feb-1 joiner, 10-day annual | → 9.17 | Feb–Dec = 11 months, rounds 9.1667 → 9.17 |
| Oct-1 joiner, 12-day annual, fiscal Apr | → 6.00 | Oct–Mar = 6 months (fiscal), not 3 (calendar) |
| Jul-1 joiner, 14-day annual (service) | → 7.00 | Jul–Dec = 6 months, 14 × 6/12 |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Pro-rata: join Jul 1, 20-day annual, calendar leave year. | `10.00`. | `LeaveEntitlementEngineTests.ProRata_JoinJuly1_20DayAnnual_Returns10` |
| 2 | Pro-rata: join Dec 31, 20-day annual. | `1.67` (join month counted as a full month). | `LeaveEntitlementEngineTests.ProRata_JoinDec31_MinimalEntitlement` |
| 3 | Pro-rata: join Feb 1, 10-day annual. | `9.17` (2dp rounding of 9.1667). | `LeaveEntitlementEngineTests.ProRata_Rounds_To2dp` |
| 4 | Pro-rata: join Oct 1, 12-day annual, `fiscalYearStartMonth = 4`. | `6.00` — months counted over the Apr–Mar fiscal year (a calendar basis would wrongly give 3.00). | `LeaveEntitlementEngineTests.ProRata_FiscalYear_OctoberJoiner_ExactMonthFraction_ISSUE034` |
| 5 | Service: compute effective entitlement for a Jul-1 joiner (14-day annual). | `ProratedEntitlementDays == 7.00`. | `LeaveEntitlementServiceTests.ComputeEffective_MidYearJoiner_ProRated_AC4` |

## 6. Postconditions
- Mid-year joiners receive a month-fraction pro-rated entitlement, correctly rounded and anchored to the tenant's leave-year basis.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test (join-month/rounding/fiscal edges)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite):**
  - `LeaveEntitlementEngineTests.ProRata_JoinJuly1_20DayAnnual_Returns10`
  - `LeaveEntitlementEngineTests.ProRata_JoinDec31_MinimalEntitlement`
  - `LeaveEntitlementEngineTests.ProRata_Rounds_To2dp`
  - `LeaveEntitlementEngineTests.ProRata_FiscalYear_OctoberJoiner_ExactMonthFraction_ISSUE034`
  - `LeaveEntitlementServiceTests.ComputeEffective_MidYearJoiner_ProRated_AC4`
- Backing suite trait: `[Trait("TC", "TC-LV-266")]`.
