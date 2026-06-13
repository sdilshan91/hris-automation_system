---
id: TC-LV-064
user_story: US-LV-003
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-13
---

# TC-LV-064: Leave submission API responds within 500ms P95

## 1. Test Objective
Verify that the leave application submission endpoint `POST /api/v1/leaves` meets the performance SLA of 500ms (P95) under representative load, including balance check, overlap detection, working-day calculation, persistence, and notification enqueue.

## 2. Related Requirements
- User Story: US-LV-003
- Non-Functional Requirements: NFR-1, NFR-2

## 3. Preconditions
- Tenant "acme" is active with a realistic dataset (e.g., 500+ employees, 1000+ existing leave requests).
- A pool of authenticated employees with `Leave.Apply` and sufficient balances.
- Balance source available (cache with DB fallback per NFR-2).
- A load-test harness (e.g., k6/JMeter) targeting the API in a production-like environment.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | POST /api/v1/leaves | Submission |
| Concurrent virtual users | 50 | Representative concurrency |
| Duration | 5 minutes sustained | -- |
| SLA (write) | <= 500ms P95 | NFR-1 |
| Payload | Valid 3-day Annual Leave request | Distinct non-overlapping dates per user |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Warm up the API and balance cache | Cache populated; JIT warmed. |
| 2 | Run 50 concurrent virtual users submitting valid leave requests for 5 minutes | Requests succeed (201) with low error rate (< 1%). |
| 3 | Measure response-time distribution | P95 latency <= 500ms; P99 reported for visibility. |
| 4 | Confirm correctness under load | Submitted requests persist correctly with Pending status and notifications queued; no duplicate/overlap data corruption. |
| 5 | Measure balance-check path with cache miss (DB fallback) | Cache-miss path still completes within an acceptable margin; record its contribution to P95. |
| 6 | Verify tenant isolation holds under load | No cross-tenant leakage in concurrent multi-tenant submissions. |

## 6. Postconditions
- P95 submission latency is within 500ms.
- No data integrity or isolation issues introduced by concurrency.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
