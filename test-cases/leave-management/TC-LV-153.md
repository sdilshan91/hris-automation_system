---
id: TC-LV-153
user_story: US-LV-008
module: Leave Management
priority: critical
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-153: Idempotency -- re-running the year-end job for the same year creates no duplicate ledger/tracking entries (NFR-3)

## 1. Test Objective
Verify the year-end carry-forward/expiry job is idempotent: re-running `ProcessLeaveYearEndJob` for the same year/period does not create duplicate `carry_forward` or `expired` ledger entries, nor duplicate `leave_carry_forward_tracking` rows (NFR-3).

## 2. Related Requirements
- User Story: US-LV-008
- Non-Functional Requirements: NFR-3
- Assumptions/Constraints: Section 10 (manual re-trigger available to HR)
- Test Hint: Section 11 (run year-end job twice; verify no duplicates)

## 3. Preconditions
- Tenant "acme"; employee "Sam" with 8 unused Annual Leave days at 2026 year-end; `carry_forward_limit = 5`.
- The job has not yet run for the 2026->2027 boundary.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Expected carry_forward entries | exactly 1 (+5) | after any number of runs |
| Expected expired entries | exactly 1 (-3) | after any number of runs |
| Expected tracking rows | exactly 1 | for (Sam, Annual, 2026->2027) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run `ProcessLeaveYearEndJob` for 2026->2027 once | One +5 `carry_forward` and one -3 `expired` entry; one tracking row created. |
| 2 | Re-run the same job for the same 2026->2027 period (manual re-trigger) | The second run is a no-op for already-processed employees -- no new ledger entries are appended (NFR-3). |
| 3 | Run a third time | Still exactly one carry_forward, one expired, one tracking row -- counts unchanged. |
| 4 | Verify Sam's balance is stable | The opening 2027 balance is computed once (5 + entitlement); repeated runs do not inflate or double-count it. |

## 6. Postconditions
- Regardless of run count, exactly one carry-forward, one expired, and one tracking entry exist for the period; balances are stable.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
