---
id: TC-LV-194
user_story: US-LV-010
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-194: Cancelling a leave in a payroll-locked period is blocked (CONDITIONAL on payroll module; AC-4)

## 1. Test Objective
Verify that an attempt to cancel a leave that falls within a finalized/locked payroll period is rejected with the message "Cannot cancel leave for a payroll-locked period. Please contact HR.", and that the non-locked cancellation path works normally. The payroll-period-lock mechanism is CONDITIONAL on the payroll module, which is not yet implemented; the non-locked path is verified now and the locked-block is recorded as conditional.

## 2. Related Requirements
- User Story: US-LV-010
- Acceptance Criteria: AC-4
- Note: Payroll-period lock is CONDITIONAL on the payroll module (not implemented). Per docs/vault/modules/leave-management.md, payroll-lock is surfaced as an API error code (`payroll_locked`) but not modelled. The error-surfacing contract and the non-locked happy path are verified now.

## 3. Preconditions
- Tenant "acme".
- Employee "Jane Smith" has an APPROVED future Annual Leave request R within a payroll period.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request R | Annual Leave, future, Approved | within a payroll period |
| Lock state | locked / unlocked | toggled per step |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | (CONDITIONAL -- payroll module present) Mark R's period payroll-locked, then attempt `POST /api/v1/leaves/{R}/cancel` | Rejected with `code = payroll_locked` and the message "Cannot cancel leave for a payroll-locked period. Please contact HR."; status stays Approved; no reversal ledger entry. Mark CONDITIONAL on the payroll module. |
| 2 | (Live -- payroll module absent) Attempt to cancel R with no period lock | Cancellation proceeds normally (status Cancelled, reversal `adjusted` entry written) -- confirming the lock is not spuriously applied when no payroll module exists. |
| 3 | Verify the error contract is surfaced verbatim | When the backend returns `code = payroll_locked`, the FE surfaces the message via toast without modelling the lock itself (per vault). |

## 6. Postconditions
- The payroll-locked block is verified by design/error-contract (CONDITIONAL on payroll); the non-locked cancellation succeeds live.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
