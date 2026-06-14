---
id: TC-ATT-043
user_story: US-ATT-004
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-043: A decided (APPROVED/REJECTED) regularization is immutable -- re-acting is blocked; audit entries cannot be modified or deleted (negative)

## 1. Test Objective
Verify BR-3 and NFR-4: once a regularization is APPROVED or REJECTED, it cannot be re-approved, re-rejected, or flipped; a second decision attempt is refused and produces no further side effects (no duplicate attendance_log mutation, no second notification). The corresponding audit-log entries are immutable -- they cannot be updated or deleted through any application path.

## 2. Related Requirements
- User Story: US-ATT-004
- Business Rule: BR-3 (decision is immutable; a new regularization must be submitted to correct)
- Non-Functional: NFR-4 (approval actions immutable in the audit log -- no deletion/modification)

## 3. Preconditions
- Tenant "acme", manager "Dana Wells" authenticated with `Attendance.Approve.Team`.
- Two of Dana's direct-report regularizations already DECIDED: one APPROVED (Jordan), one REJECTED (Morgan).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Approved request | Jordan's APPROVED regularization_id | re-act target |
| Rejected request | Morgan's REJECTED regularization_id | re-act target |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana, `POST .../{jordan_approved_id}/reject` with a valid reason | Refused (409/422) -- a decided request cannot be changed (BR-3); status stays APPROVED. |
| 2 | As Dana, `POST .../{jordan_approved_id}/approve` again | Refused (idempotent/conflict) -- no second attendance_log mutation, no second notification dispatched. |
| 3 | As Dana, `POST .../{morgan_rejected_id}/approve` | Refused -- a REJECTED request cannot be flipped to APPROVED; status stays REJECTED. |
| 4 | Re-fetch both requests | Statuses unchanged (APPROVED / REJECTED); workflow history shows only the original single decision each. |
| 5 | Attempt to modify/delete the approval audit_log entry via any application endpoint | No application path allows updating or deleting the audit entry; the original entry (actor, timestamp, comment) is preserved (NFR-4). |

## 6. Postconditions
- Decided requests stay frozen; no duplicate side effects occur on re-attempts; audit entries are immutable.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
