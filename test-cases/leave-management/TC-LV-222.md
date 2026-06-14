---
id: TC-LV-222
user_story: US-LV-011
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-222: LOP entries are immutable once payroll is finalized for the period (NFR-3 / BR-5 — CONDITIONAL on payroll lock)

## 1. Test Objective
Verify NFR-3/BR-5: once payroll is finalized (locked) for a period, the LOP entries in that period cannot be edited, removed, or overridden. Attempts to modify (e.g. convert/remove a System-Generated LOP, re-assign, or delete) are rejected. The payroll-period lock DEPENDS on the Payroll module (US-PAYROLL-*); the non-locked editable path is verified live and the locked-rejection is recorded CONDITIONAL.

## 2. Related Requirements
- User Story: US-LV-011
- Non-Functional Requirements: NFR-3
- Business Rules: BR-5
- Dependency: US-PAYROLL-* (period finalize/lock) — CONDITIONAL
- Test Hint §11 (payroll lock)

## 3. Preconditions
- Tenant "acme"; employee "Mark Otieno" has LOP entries in month M.
- HR Officer "Asha" authenticated with `Leave.Manage`/`HR.Officer`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Period M (unlocked) | editable | non-locked path |
| Period M (locked) | finalized | immutable |
| Error code | payroll_locked | surfaced |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | (Live, non-locked) While month M is NOT payroll-finalized, override/remove an LOP entry | Allowed (per BR-3, TC-LV-221) — establishes the editable baseline. |
| 2 | (CONDITIONAL) Finalize/lock payroll for month M, then attempt to convert/remove/re-assign an LOP entry in M | Rejected with a clear error (e.g. `payroll_locked`); the LOP entry is unchanged. Mark CONDITIONAL on the payroll-period-lock signal (US-PAYROLL-*). |
| 3 | (CONDITIONAL) Attempt to delete a finalized-period LOP entry | Rejected; immutability enforced. |
| 4 | Confirm error contract surfaced | The `payroll_locked` error message is surfaced to the HR UI (not a silent failure). |

## 6. Postconditions
- LOP entries in a finalized payroll period are immutable; the editable path (non-locked) is verified live; the lock-rejection is conditional on payroll.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
