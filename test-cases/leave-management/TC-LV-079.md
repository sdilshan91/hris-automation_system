---
id: TC-LV-079
user_story: US-LV-004
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-079: A newly submitted request appears after queue refresh; real-time SignalR push is dependent on the notifications module

## 1. Test Objective
Verify that when a team member submits a new leave request while the manager is viewing the queue, the new request is included after a queue reload (manual refresh or banner-prompted refresh re-querying the API). The real-time SignalR push that auto-prompts/auto-refreshes is verified at the API-reload level here and the push expectation is marked dependent on the SignalR/notifications module rather than silently passed.

## 2. Related Requirements
- User Story: US-LV-004
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6
- Dependencies: SignalR hub `/hubs/notifications` (notifications module) -- DEFERRED for real-time push

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team` and is viewing the pending queue.
- Direct report "Jane Smith" is able to submit a leave request (US-LV-003).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Initial queue size | 3 | Before new submission |
| New request | Jane Smith, Sick Leave, 2026-07-20, 1 day | Submitted during viewing |
| Hub | /hubs/notifications | Real-time channel (deferred) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With Robert viewing the queue (3 items), have Jane submit a new pending leave request via `POST /api/v1/leaves` | The request is created with status Pending (US-LV-003). |
| 2 | Trigger a queue reload (manual refresh or click the "refresh" banner action) | `GET /api/v1/leaves/pending` is re-issued and now returns 4 items including Jane's new request. |
| 3 | Verify the new request renders correctly | Jane's new Sick Leave request appears with all inline fields and respects the current sort/filter. |
| 4 | Verify real-time push behavior | IF the SignalR notifications hub is implemented: a real-time event prompts a refresh banner (or auto-refreshes) without a manual reload. IF the notifications module is NOT yet implemented: this real-time-push expectation is marked DEPENDENT/DEFERRED on the SignalR notifications module -- documented, NOT silently passed. The API-reload path in steps 1--3 still verifies the queue includes new requests. |

## 6. Postconditions
- No unintended data mutation beyond Jane's new request.
- The queue reflects newly submitted requests on reload; real-time push is conditional on the notifications module.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
