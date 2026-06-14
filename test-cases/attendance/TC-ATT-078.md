---
id: TC-ATT-078
user_story: US-ATT-006
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-078: Decided overtime records are immutable -- a decided (APPROVED/REJECTED) record cannot be re-decided or silently altered (security)

## 1. Test Objective
Verify the decision-immutability invariant (analogue of US-ATT-004 BR-3/NFR-4): once an overtime_record is APPROVED or REJECTED, it cannot be re-approved, re-rejected, or re-adjusted to produce a second decision/side effect; the audit entries for the decision cannot be modified or deleted.

## 2. Related Requirements
- User Story: US-ATT-006
- Acceptance Criteria: AC-4 (decision integrity)
- Functional Requirements: FR-6 (a single binding decision), FR-7 (payroll-ready must not flip-flop)
- Non-Functional: NFR-3 (auditable -- audit entries immutable)

## 3. Preconditions
- Tenant "acme". Manager "Ben"; an APPROVED overtime_record (approved_minutes = 60) and a REJECTED overtime_record exist for direct reports.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| approved record | status APPROVED, approved_minutes 60 | |
| rejected record | status REJECTED | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Ben, `POST /overtime/{approvedId}/approve` again (or /reject) | Refused -- the record is already decided; no second decision, no duplicate payroll-ready/audit side effect. |
| 2 | As Ben, `POST /overtime/{rejectedId}/approve` | Refused -- a rejected record cannot be flipped to approved via the manager endpoint (HR override is a separate, audited path if it exists). |
| 3 | Attempt to alter approved_minutes on a decided record via the approve endpoint | Refused / no-op; the decided value is stable. |
| 4 | Attempt to modify or delete the decision's audit entry | Refused -- audit log is append-only/immutable (NFR-3). |
| 5 | Verify no duplicate records or balances | Re-acting produces no extra overtime_record and does not double-count payroll-ready minutes. |

## 6. Postconditions
- Decided overtime records and their audit trail are immutable via the normal manager endpoints; no duplicate decisions or side effects occur.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Consistent with US-ATT-004 TC-ATT-043 (regularization decision immutability). If the story later defines an HR override/re-open path, it is a distinct, audited operation and should get its own TC; the manager approve/reject endpoints remain single-decision.
