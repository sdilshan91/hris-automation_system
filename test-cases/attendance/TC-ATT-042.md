---
id: TC-ATT-042
user_story: US-ATT-004
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-042: A manager cannot approve their own regularization -- it is absent from their actionable queue and self-approval is blocked (negative)

## 1. Test Objective
Verify BR-6: a manager's OWN regularization request does not appear in their own actionable approval queue and cannot be self-approved/self-rejected. The request instead routes to the manager's supervisor (or HR) per the configured chain. Attempting to self-approve via the API is refused with no state change.

## 2. Related Requirements
- User Story: US-ATT-004
- Business Rule: BR-6 (managers cannot approve their own requests; route to their supervisor/HR)
- Functional Requirements: FR-1 (queue scope), FR-4 (workflow routing), FR-7 (direct-report scope)

## 3. Preconditions
- Tenant "acme". Manager "Dana Wells" authenticated with `Attendance.Approve.Team`.
- Dana reports to "Priya Nair" (Dana's supervisor) per Core HR `manager_id`.
- Dana has submitted her OWN PENDING `attendance_regularization` (id known).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Self request | Dana's own PENDING regularization_id | should route to Priya |
| Acting user | Dana Wells | attempting to self-approve |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana, open the approval queue (`GET .../approval-queue`) | Dana's OWN request is NOT listed -- a manager's own request never appears in their own actionable queue (BR-6). |
| 2 | As Dana, attempt `POST .../regularizations/{own_id}/approve` | Request is refused (403/422) -- self-approval is blocked; status stays PENDING; no attendance_log change. |
| 3 | As Priya Nair (Dana's supervisor) open the approval queue | Dana's request IS listed under Priya -- it routed up the chain per FR-4/BR-6. |
| 4 | As Priya, approve Dana's request | Response 200; status APPROVED; the decision actor is Priya, not Dana. |
| 5 | Verify audit | Dana's self-approval attempt (step 2) and Priya's approval (step 4) are both recorded; the approver of record is Priya. |

## 6. Postconditions
- A manager's own request is excluded from their actionable queue and cannot be self-approved; it is actionable only by their supervisor/HR.

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
- The routing-to-supervisor side (steps 3-4) depends on the Approval Workflow Engine for configurable chains; with the single-level default the "route to supervisor/HR" target is the manager's own `manager_id`. The deny-self-approval invariant (steps 1-2) is verifiable now regardless of workflow depth. Multi-level chain configuration is DEFERRED -- see TC-ATT-044.
