---
id: TC-LV-096
user_story: US-LV-005
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-096: Concurrent approve/reject on the same request -- only the first succeeds; the second gets a concurrency conflict (xmin optimistic concurrency)

## 1. Test Objective
Verify that when two approvers (or two browser sessions) submit decisions on the same pending request simultaneously, only the first decision is committed and the second receives a concurrency-conflict error ("This request has already been actioned"), enforced via PostgreSQL `xmin` optimistic concurrency (EF Core `UseXminAsConcurrencyToken()`). This prevents double-approval and approve-then-reject races (AC-5, FR-6, NFR-4).

## 2. Related Requirements
- User Story: US-LV-005
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" is active.
- A single pending request R from a direct report exists, status `Pending`, with a known `xmin` value.
- Two authorized sessions (e.g., the primary manager and a co-manager who is also a valid approver, or the same manager in two tabs) can both target R.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request R | status Pending | Single shared row |
| Session 1 | approve | Submitted concurrently |
| Session 2 | reject (or approve) | Submitted concurrently |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Both sessions load R while it is Pending (both observe the same `xmin`). | Both hold the same concurrency token. |
| 2 | Session 1 submits `POST /api/v1/leaves/{R}/approve`; Session 2 submits `POST /api/v1/leaves/{R}/reject` at the same instant. | Exactly one request commits first and changes R's `xmin`. |
| 3 | Observe Session 1 (first to commit) | Returns 200; R transitions to the corresponding terminal state (e.g., Approved) and its ledger/history/audit side effects occur exactly once. |
| 4 | Observe Session 2 (second to commit) | Returns a concurrency-conflict error (HTTP 409) with the message "This request has already been actioned"; no second transition, no second ledger entry, no second history row. |
| 5 | Repeat the race with both sessions submitting `approve` (double-approval) | Only one approval commits; the other gets 409; exactly one `used` ledger entry exists. |
| 6 | Verify final DB state | R has a single terminal status and exactly one corresponding `leave_approval_history` row and (if approved) one `used` ledger entry. |

## 6. Postconditions
- R has exactly one decision applied; the losing request is rejected with 409 and leaves no side effects.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
