---
id: TC-ATT-032
user_story: US-ATT-003
module: Attendance
priority: high
type: integration
status: draft
created: 2026-06-14
---

# TC-ATT-032: Manager receives an in-app notification when a regularization is submitted (CONDITIONAL/DEFERRED on Notification System)

## 1. Test Objective
Verify FR-4: when an employee submits a regularization request, the approver (line manager) is notified in-app (and optionally by email). Because the Notification System (US-NTF) is NOT yet built, this TC verifies the integration SEAM now -- that submission triggers a notification dispatch (e.g., a queued notification command or a log-only/no-op notifier with the correct recipient = the employee's line manager, correct tenant scope, and a payload referencing the `regularization_id`) -- and DEFERS the end-to-end in-app delivery and badge-count assertions until the notifications module exists. This mirrors how leave-management notification dispatch was handled as a log-only seam DEFERRED on the notifications module, and how the Redis cache path was treated as CONDITIONAL in TC-ATT-001/TC-ATT-013.

## 2. Related Requirements
- User Story: US-ATT-003
- Functional Requirements: FR-4
- Business Rules: BR-1 (request requires at least one level of approval -> approver is the notification recipient)
- Dependencies: Notification System (US-NTF) -- NOT yet implemented

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, regularization workflow configured (single-level: line manager).
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Regularize.Self`, and reports to manager "Pat Kim".
- Notification System availability is recorded for the run (present vs. seam-only).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Submitter | Jordan Lee (acme) | Has a line manager |
| Approver / recipient | Pat Kim (Jordan's line manager) | Expected notification target |
| Notification module | seam-only (deferred) OR live | Branch the assertions |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jordan Lee, submit a valid regularization (per TC-ATT-025) | Response 201 Created; a PENDING regularization with a workflow instance is created. |
| 2 | Inspect the notification seam (queue/outbox/log) | A single notification dispatch is recorded targeting the line manager (Pat Kim), tenant-scoped to acme, referencing the `regularization_id` and the requested date. The recipient is the workflow approver, not the submitter. |
| 3 | (Notification module present) Verify in-app delivery | Pat Kim's in-app notification list shows a "Regularization request from Jordan Lee" item with an unread badge; opening it deep-links to the approval (US-ATT-004). |
| 4 | (Notification module NOT present -- current state) Record DEFERRED | Confirm the dispatch seam fired with the correct recipient/tenant/payload (or is a no-op notifier producing nothing) and mark in-app delivery + badge-count assertions DEFERRED until US-NTF lands. This is NOT a coverage gap -- the seam is verified now and end-to-end delivery is re-run once notifications exist. |
| 5 | Verify tenant scoping of the recipient resolution | The approver is resolved within tenant acme only; no cross-tenant manager can be targeted (see TC-ATT-ISO-006). |

## 6. Postconditions
- A tenant-scoped notification dispatch to the line manager is recorded on submit (seam verified); in-app delivery is verified now if the module exists, else DEFERRED.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
