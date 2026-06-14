---
id: TC-ATT-044
user_story: US-ATT-004
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-044: Multi-level workflow -- a first-level approval keeps the request PENDING and advances to the next approver; attendance_log written only on final approval (CONDITIONAL/DEFERRED)

## 1. Test Objective
Verify AC-4/FR-4/BR-4: where a multi-level approval chain is configured and the manager is the first approver, the manager's approval advances the workflow to the next approver (e.g. HR) and the regularization stays PENDING until the final level approves; the `attendance_log` is created/updated ONLY at the final approval. This TC is CONDITIONAL/DEFERRED because the Approval Workflow Engine (US-ADM-007) that drives configurable multi-level chains is not yet built; the single-level final-approval path is verified live in TC-ATT-037.

## 2. Related Requirements
- User Story: US-ATT-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-4 (advance workflow per the tenant's configured chain)
- Business Rule: BR-4 (attendance_log updated only on the FINAL approval)
- Dependency: Approval Workflow Engine (US-ADM-007)

## 3. Preconditions
- Tenant "acme" with a TWO-level chain configured for attendance regularization: Level 1 = line manager (Dana), Level 2 = HR (Priya).
- A PENDING `attendance_regularization` exists for Dana's direct report Jordan, currently at workflow step level 1.
- **CONDITIONAL:** requires the Workflow Engine to support configurable multi-level chains. If unavailable, run only the single-level assertions and record the multi-level steps as DEFERRED (do NOT pass them silently).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Chain | L1 manager -> L2 HR | tenant-configured |
| L1 approver | Dana Wells | first approver |
| L2 approver | Priya (HR) | final approver |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana (L1), approve the request | Response 200; the workflow advances to L2 (HR); the regularization status remains PENDING (NOT APPROVED) -- AC-4/BR-4. |
| 2 | Inspect attendance_log after L1 approval | NO attendance_log is created/updated yet -- the log is written only on final approval (BR-4). |
| 3 | Inspect workflow history | The L1 step is recorded as completed by Dana with timestamp; the active step is now L2. |
| 4 | As Priya (L2/final), approve the request | Status becomes APPROVED; NOW the attendance_log is created/updated with regularized times and total_work_minutes recalculated (as in TC-ATT-037). |
| 5 | Single-level control (live) | In a tenant with a single-level chain, the manager's approval immediately finalizes (APPROVED) and writes the log -- the verified-now default; this is the live coverage while multi-level is DEFERRED. |

## 6. Postconditions
- With multi-level chains, intermediate approvals keep the request PENDING and defer the attendance_log write until the final level; single-level finalizes immediately.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **DEFERRED on US-ADM-007 (Approval Workflow Engine).** Steps 1-4 (multi-level routing + final-only log write) are CONDITIONAL on the engine supporting configurable N-level chains; step 5 (single-level default) is the live-verifiable path. Consistent with how US-LV-005 TC-LV-097 and US-ATT-003 handled the workflow-engine dependency. **Reported to caller.**
