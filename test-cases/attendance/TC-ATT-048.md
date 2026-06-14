---
id: TC-ATT-048
user_story: US-ATT-004
module: Attendance
priority: high
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-048: Approval and rejection actions are recorded in the audit log with manager id, timestamp, and comment (security/audit)

## 1. Test Objective
Verify FR-6/NFR-4: every approve and reject action writes an audit_log entry capturing the manager's user id (actor), a decision timestamp, the action type, the target regularization_id, and the comment (the optional approval comment or the mandatory rejection reason). Entries are tenant-scoped and immutable (no update/delete via the application -- cross-ref TC-ATT-043).

## 2. Related Requirements
- User Story: US-ATT-004
- Functional Requirements: FR-6 (log the approval/rejection with manager id, timestamp, comment)
- Non-Functional: NFR-4 (audit entries immutable)

## 3. Preconditions
- Tenant "acme", manager "Dana Wells" authenticated with `Attendance.Approve.Team`.
- Two PENDING requests for Dana's direct reports: one to approve (Jordan), one to reject (Morgan).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Approve | Jordan's request, comment "Confirmed via roster." | optional comment present |
| Reject | Morgan's request, reason "Times do not match badge logs." | mandatory reason |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana, approve Jordan's request with a comment | An audit_log row is written: action=approve (or equivalent), actor_user_id=Dana, UTC timestamp, regularization_id=Jordan's, comment captured, tenant_id=acme. |
| 2 | As Dana, reject Morgan's request with a reason | An audit_log row is written: action=reject, actor=Dana, timestamp, regularization_id=Morgan's, the rejection reason captured, tenant_id=acme. |
| 3 | Query audit_log for these two regularizations | Exactly one decision entry each; fields are complete and correct; both scoped to acme. |
| 4 | Attempt to update/delete an audit entry via any application route | Refused -- audit entries are immutable (NFR-4). |
| 5 | Cross-tenant audit visibility | A globex user cannot read acme's audit entries for these actions (tenant-scoped -- cross-ref TC-ATT-ISO-007). |

## 6. Postconditions
- Approve/reject actions are fully and immutably audited with actor, timestamp, target, and comment, tenant-scoped.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
