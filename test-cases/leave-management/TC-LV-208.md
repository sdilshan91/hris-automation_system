---
id: TC-LV-208
user_story: US-LV-010
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-LV-208: Cancellation API responds within 500ms P95 (performance; NFR-1)

## 1. Test Objective
Verify that the cancellation endpoint `POST /api/v1/leaves/{id}/cancel` -- including the status update, reversal `adjusted` ledger write (for approved), approval-history insert, audit write, and notification queue -- completes within 500ms at the 95th percentile under representative load (NFR-1).

## 2. Related Requirements
- User Story: US-LV-010
- Non-Functional Requirements: NFR-1
- Note: Redis balance-cache invalidation (FR-4) is DEFERRED module-wide; the measured path uses the DB LeaveLedger running total.

## 3. Preconditions
- Tenant "acme" seeded with a realistic dataset (e.g. 1,000+ employees, populated LeaveLedger).
- A pool of cancellable requests (mix of pending and approved-future) is available.
- A load tool (k6/JMeter) issues concurrent authenticated cancel calls.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| SLA | <= 500ms P95 | NFR-1 |
| Mix | ~50% pending (no ledger), ~50% approved (reversal write) | representative |
| Concurrency | sustained representative load | warmed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Warm up, then run the cancel load against the request pool | Endpoint responds successfully; no errors under load. |
| 2 | Measure latency percentiles | P95 latency <= 500ms; P99 captured for reference. |
| 3 | Compare pending vs approved cancellations | Both stay within SLA; the approved path (extra reversal ledger + history writes) does not breach 500ms P95. |
| 4 | Verify correctness under load | Each cancelled request has exactly one Cancelled transition and (for approved) exactly one reversal entry -- no duplication or balance drift under concurrency. |

## 6. Postconditions
- Cancellation P95 latency is within 500ms; correctness holds under load.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
