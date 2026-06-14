---
id: TC-LV-155
user_story: US-LV-008
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-155: Leave types with unlimited / non-applicable balance are skipped by carry-forward processing (BR-6)

## 1. Test Objective
Verify that the year-end carry-forward/expiry job skips leave types that do not maintain a finite balance -- e.g. unpaid leave or types with no carry-forward concept -- so no spurious `carry_forward` or `expired` entries are generated for them (BR-6).

## 2. Related Requirements
- User Story: US-LV-008
- Business Rules: BR-6
- Cross-reference: US-LV-001 (Unpaid Leave: 0 entitlement, negative-balance allowed)

## 3. Preconditions
- Tenant "acme" with:
  - "Unpaid Leave" (unlimited / negative-balance-allowed, not balance-tracked for carry-forward).
  - "Annual Leave" (`carry_forward_limit = 5`) for contrast.
- Employee "Sam" has activity on both types in 2026.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Unpaid Leave | unlimited / non-applicable | skipped |
| Annual Leave | carry_forward_limit 5 | processed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run `ProcessLeaveYearEndJob` for the 2026->2027 boundary | No `carry_forward` or `expired` ledger entries are created for Unpaid Leave (BR-6). |
| 2 | Verify Annual Leave is still processed | Annual Leave carry-forward/expiry runs normally (contrast control), confirming the skip is type-specific not a global no-op. |
| 3 | Inspect Unpaid Leave ledger | Unpaid Leave ledger is unchanged by the year-end job. |
| 4 | Confirm job log | The job records Unpaid Leave as skipped (not-applicable), not as an error. |

## 6. Postconditions
- Unlimited / non-applicable leave types are untouched by carry-forward processing; balance-tracked types are processed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
