---
id: TC-LV-167
user_story: US-LV-008
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-167: Expiry calculated from the first day of the new leave year; tenant fiscal-year boundary (BR-3, Section 10; fiscal-year CONDITIONAL)

## 1. Test Objective
Verify carry-forward expiry is anchored to the first day of the new leave year (BR-3), and that the leave-year boundary respects the tenant's configured cycle (calendar year default; fiscal year per Section 10). The fiscal-year boundary is CONDITIONAL on a tenant fiscal-year config existing; with the calendar-year default it is verified directly.

## 2. Related Requirements
- User Story: US-LV-008
- Business Rules: BR-3
- Assumptions/Constraints: Section 10 (leave year boundary configurable per tenant; default Jan 1 - Dec 31)
- Note: No tenant fiscal-year config entity exists yet (per docs/vault/modules/leave-management.md ResolveLeaveYear uses calendar year). Fiscal-year anchoring is CONDITIONAL/forward-looking; calendar-year anchoring is verified now.

## 3. Preconditions
- Tenant "acme" on the default calendar-year cycle; Annual Leave `carry_forward_expiry_months = 3`.
- Employee "Sam" with 5 carry-forward days from the 2026 year-end.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Calendar-year start | 2027-01-01 | default |
| Expiry (calendar) | 2027-03-31 | start + 3 months |
| Fiscal-year start (hypothetical) | 2027-04-01 | CONDITIONAL |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With the calendar-year cycle, inspect the carry-forward expiry date | Expiry = first day of new leave year (2027-01-01) + 3 months = 2027-03-31 (BR-3). |
| 2 | Run the expiry job just after 2027-03-31 | The unused carry-forward expires (anchored to the calendar-year start), confirming the anchor is the new leave year's first day. |
| 3 | (CONDITIONAL) Configure a fiscal-year cycle starting 2027-04-01 (when tenant fiscal-year config exists) | Expiry should anchor to 2027-04-01 + 3 months = 2027-06-30. Record as CONDITIONAL/BLOCKED pending the tenant fiscal-year config entity (TODO(tenant-settings)). |
| 4 | Confirm honest deferral | With no fiscal-year config in the codebase today, only the calendar-year path is exercised live; the fiscal-year path is documented as dependent, not silently passed. |

## 6. Postconditions
- Expiry anchors to the new leave year's first day (calendar-year verified); fiscal-year anchoring is conditional on tenant config.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
