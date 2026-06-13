---
id: TC-LV-107
user_story: US-LV-005
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-107: Notification queuing is asynchronous and best-effort -- the decision commits even if notification queuing fails

## 1. Test Objective
Verify that approve/reject notification queuing is asynchronous and non-blocking (NFR-2) and best-effort (Section 10): the decision (status change, ledger, history, audit) is committed even when the notification seam fails, and the API still returns success. NOTE: the notification dispatch itself is the log-only `ILeaveNotificationService` seam (DEFERRED on the notifications module).

## 2. Related Requirements
- User Story: US-LV-005
- Non-Functional Requirements: NFR-2
- Assumptions & Constraints (Section 10): notification delivery is best-effort with Polly retry on the notification side
- Dependencies: Notification Service -- DEFERRED

## 3. Preconditions
- Tenant "acme" is active; Manager authenticated with `Leave.Approve.Team`.
- A pending request from a direct report exists.
- The notification seam can be stubbed to simulate a queuing failure.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Notification seam | Forced to fail | Simulated outage |
| Request | Pending | To be approved |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With the notification seam stubbed to throw, approve the request | API returns 200; the request is Approved, the `used` ledger entry is created, history and audit are written -- the decision is committed despite the notification failure. |
| 2 | Inspect logs | The notification failure is logged/queued for retry (Polly on the notification side, per Section 10); it does not roll back the decision. |
| 3 | Measure the response with the notification path active vs stubbed | Response time is comparable -- notification queuing is off the request path (NFR-2, async). |
| 4 | NOTE the deferral | Actual notification delivery is DEFERRED on the notifications module; this TC verifies the decision commits independently of notification success and that queuing is non-blocking. |

## 6. Postconditions
- Decision committed regardless of notification queuing outcome; failure logged for retry.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
