---
id: TC-LV-052
user_story: US-LV-003
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-052: Overlapping leave dates with an existing Pending/Approved request are rejected

## 1. Test Objective
Verify that when an Employee submits a leave request whose date range overlaps an existing Pending or Approved request for the same employee, the system rejects the new request with a clear message and does not create it. (Test Hint: create two overlapping leave requests; verify the second is rejected.)

## 2. Related Requirements
- User Story: US-LV-003
- Acceptance Criteria: AC-5
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" is active; Employee "Jane Smith" is authenticated with `Leave.Apply`.
- Jane already has an existing leave request: Annual Leave, 2026-07-06 to 2026-07-10, status "Pending".
- Jane has sufficient balance to otherwise pass balance validation.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Existing request | Annual Leave 2026-07-06 to 2026-07-10 (Pending) | Pre-existing |
| New request A | Annual Leave 2026-07-08 to 2026-07-12 | Overlaps existing (partial) |
| New request B | Annual Leave 2026-07-10 to 2026-07-10 | Overlaps on boundary day |
| New request C | Annual Leave 2026-07-13 to 2026-07-15 | No overlap (adjacent, valid) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Leave Application page; select Annual Leave with dates 2026-07-08 to 2026-07-12 (New request A) | Calendar/date picker visually marks 2026-07-06..10 as existing leave. |
| 2 | Submit New request A | Validation error: "You already have a leave request for the selected dates." Response 400/422. No new request created. |
| 3 | Submit New request B (single overlapping boundary day 2026-07-10) | Same overlap rejection -- boundary day counts as overlap. |
| 4 | Submit New request C (2026-07-13 to 2026-07-15, no overlap) | Request accepted; 201 Created; new Pending request exists. |
| 5 | Cancel/reject the original Pending request, then resubmit New request A | New request A is now accepted (no Pending/Approved overlap remains). |
| 6 | Verify overlap check ignores Rejected/Cancelled requests | A Rejected request on the same dates does not block the new submission. |

## 6. Postconditions
- No overlapping request is created for requests A and B.
- The non-overlapping request C is created with status "Pending".

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
