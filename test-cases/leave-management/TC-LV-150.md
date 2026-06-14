---
id: TC-LV-150
user_story: US-LV-008
module: Leave Management
priority: critical
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-150: Zero carry-forward limit -> all unused balance forfeited as expired entries (AC-4, BR-1, BR-2, FR-6)

## 1. Test Objective
Verify that when a leave type is configured with `carry_forward_limit = 0`, the year-end job carries nothing forward and forfeits the entire unused balance as `expired` ledger entries (AC-4, BR-1, BR-2, FR-6).

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-4
- Functional Requirements: FR-2, FR-6
- Business Rules: BR-1, BR-2

## 3. Preconditions
- Tenant "acme" with a "Casual Leave" type configured: `carry_forward_limit = 0`.
- Employee "Sam" ends the 2026 leave year with 4 unused Casual Leave days.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Unused balance at year-end | 4 days | -- |
| carry_forward_limit | 0 | no carry-forward |
| Expected carried forward | 0 days | MIN(4, 0) |
| Expected expired | 4 days | full forfeiture |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run `ProcessLeaveYearEndJob` for the 2026->2027 boundary | NO `carry_forward` ledger entry is created for Casual Leave (MIN(4,0)=0). |
| 2 | Inspect the forfeiture | One `expired` ledger entry: days = -4, `transaction_type='expired'` (AC-4, BR-2, FR-6). |
| 3 | Read Sam's opening 2027 Casual Leave balance | Opening balance = 0 carry-forward + new entitlement only; none of the 4 days survive. |
| 4 | Confirm idempotent-safe state | Re-reading the ledger shows exactly one expired entry of -4 for the period (no partial carry-forward). |

## 6. Postconditions
- All 4 unused Casual Leave days are expired; nothing carried forward.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
