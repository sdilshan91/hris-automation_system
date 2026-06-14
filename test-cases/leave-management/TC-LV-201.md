---
id: TC-LV-201
user_story: US-LV-010
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-201: Concurrent manager-approve vs employee-cancel on the same request -- only one operation succeeds (xmin optimistic concurrency -> 409) (NFR-3, Test Hint)

## 1. Test Objective
Verify that when a manager submits an approval at the same instant the employee submits a cancellation on the same pending request, exactly one operation commits and the other receives a concurrency-conflict (HTTP 409), enforced via PostgreSQL `xmin` optimistic concurrency. This prevents the race where a leave is both approved and cancelled (NFR-3, Section 10 assumption).

## 2. Related Requirements
- User Story: US-LV-010
- Non-Functional Requirements: NFR-3
- Assumptions: Section 10 (concurrency prevents approve-while-cancel)

## 3. Preconditions
- Tenant "acme".
- A single PENDING request R from employee "Jane Smith" exists, with a known `xmin` value.
- Manager "Robert Lee" (approver) and Jane (owner) both have R loaded at the same `xmin`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request R | Annual Leave, Pending | single shared row |
| Session M | Robert -> approve | concurrent |
| Session E | Jane -> cancel | concurrent |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Both sessions read R while Pending (same `xmin`). | Both hold the same concurrency token. |
| 2 | Robert submits `POST /api/v1/leaves/{R}/approve` and Jane submits `POST /api/v1/leaves/{R}/cancel` at the same instant. | Exactly one commits first and changes R's `xmin`. |
| 3 | Observe the first to commit | Returns 200; R transitions to that terminal state (Approved OR Cancelled) and its side effects occur exactly once. |
| 4 | Observe the second to commit | Returns HTTP 409 (concurrency conflict / "already been actioned"); no second transition. |
| 5 | If approve won then cancel lost | R is Approved with a `used` entry; the late cancel gets 409 and writes no reversal. (Jane may then cancel the now-Approved future R via the normal flow -- a separate, sequential operation.) |
| 6 | If cancel won then approve lost | R is Cancelled (no `used` entry written since it was pending); the late approve gets 409 and creates no `used` ledger entry. |
| 7 | Verify final DB state | R has exactly one terminal status; ledger and approval-history reflect only the winning operation (no mixed/duplicated side effects). |

## 6. Postconditions
- Exactly one of approve/cancel applies; the loser gets 409 with no side effects; the leave is never simultaneously approved and cancelled.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
