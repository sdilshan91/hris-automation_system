---
id: TC-ATT-069
user_story: US-ATT-006
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-069: Overtime multiplier by day type -- weekday 1.5x, weekend (rest day) 2.0x, public holiday 2.5x (boundary)

## 1. Test Objective
Verify FR-3/BR-3/BR-7: the overtime_record stores the correct tenant-configurable multiplier per day type -- weekday 1.5x (default), weekend/rest day 2.0x, public holiday 2.5x -- and that the multiplier is recorded on the record (it is APPLIED later, in payroll, not at attendance recording time, per S10 assumptions).

## 2. Related Requirements
- User Story: US-ATT-006
- Functional Requirements: FR-3 (tenant-configurable multiplier rates incl. weekend/holiday)
- Business Rules: BR-3 (weekday 1.5x, weekend 2.0x, public holiday 2.5x defaults), BR-7 (rest day / public holiday may have different rates)
- Assumptions/Constraints: S10 (multiplier applied during payroll, not during attendance recording)

## 3. Preconditions
- Tenant "acme" with overtime rules: weekday 1.50, weekend 2.00, public_holiday 2.50.
- Employee "Asha" assigned a shift whose working_days make Sat/Sun rest days.
- A public holiday is configured for a known date (integrates the holiday calendar -- see Notes for the dependency).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| weekday multiplier | 1.50 | BR-3 default |
| weekend multiplier | 2.00 | BR-3 / BR-7 |
| public_holiday multiplier | 2.50 | BR-3 / BR-7 |
| OT minutes each day | 60 | enough to exceed standard + threshold |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Generate overtime on a weekday (clock out 1h over) | overtime_record.multiplier = 1.50. |
| 2 | Generate overtime on a Saturday (rest day) | overtime_record.multiplier = 2.00 (the rest-day rate, BR-7), not 1.50. |
| 3 | Generate overtime on a configured public holiday | overtime_record.multiplier = 2.50. |
| 4 | Confirm the multiplier is STORED, not applied to pay at recording time | The overtime_record carries the multiplier (decimal(3,2)); no monetary amount is computed here -- the rate is applied in payroll (S10). |
| 5 | Change the tenant weekend rate to 1.75 and re-run Step 2 | A new weekend overtime_record shows multiplier = 1.75 -- rates are tenant-configurable (FR-3). |
| 6 | Day with both weekend AND public-holiday status | The higher/holiday rate (2.50) applies, or per the tenant's configured precedence -- assert the documented precedence, not an accidental one (see Notes). |

## 6. Postconditions
- Overtime records carry the day-type-appropriate, tenant-configurable multiplier; no pay is computed at attendance time.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Public-holiday source (Step 3/Step 6):** the holiday calendar lives in Leave Management (US-LV-007, implemented). The attendance overtime detector must consult a tenant holiday source to classify a date as a public holiday. If that integration is not yet wired in the Attendance module, Step 3 and the holiday side of Step 6 are CONDITIONAL on the holiday-source integration -- the weekday/weekend classification (Steps 1-2, derived from shift working_days) is verifiable independently now. **Reported to caller.**
- **Weekend+holiday precedence (Step 6):** assert against the documented tenant precedence rule; if undocumented, flag to the BA rather than freezing an arbitrary order.
