---
id: TC-ATT-115
user_story: US-ATT-008
module: Attendance
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-ATT-115: Performance -- late/early report loads < 2s P95 @500 employees (NFR-3); inline late/early detection adds no measurable clock-in/out latency (NFR-1)

## 1. Test Objective
Verify NFR-3 and NFR-1: the late/early-departure report returns within 2 seconds at P95 for a tenant with up to 500 employees over a one-month period; and late/early detection, being computed inline within the clock-in/out transaction, adds no measurable additional latency versus a baseline punch (no extra round-trip).

## 2. Related Requirements
- User Story: US-ATT-008
- Non-Functional: NFR-3 (report < 2s P95 @500 employees), NFR-1 (inline detection, no added latency)
- API: GET /late-early/report?from=&to=&scope=all

## 3. Preconditions
- Tenant "perftest" seeded with 500 active employees on SINGLE/ROTATING shifts and a full month of attendance_logs carrying is_late/early flags.
- Warm DB connection pool; representative hardware; a load tool capable of P95 measurement over >= 100 iterations.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| employee count | 500 | NFR-3 ceiling |
| period | one month | report window |
| iterations | >= 100 | for P95 |
| report SLA | < 2000 ms P95 | NFR-3 |
| inline detection overhead | ~0 vs baseline punch | NFR-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run `GET /late-early/report?from=...&to=...&scope=all` (HR) >= 100 times under representative load | P95 latency < 2000 ms (NFR-3); no timeouts; results stable across runs. |
| 2 | Measure a clock-in for a fixed shift (late/early evaluated) vs a baseline clock-in with detection disabled/no shift | The latency delta is within noise -- detection is inline (NFR-1), adding no separate DB round-trip or API call. |
| 3 | Measure clock-out (early-departure evaluated) similarly | Same -- no measurable added latency from the early-departure computation. |
| 4 | Confirm query efficiency | The report uses tenant-scoped, indexed aggregation over attendance_log (is_late/is_early_departure), not N+1 per-employee queries; record the query plan if available. |
| 5 | Department-filtered variant | Filtering to a department does not regress P95 (narrower scope <= full-scope latency). |

## 6. Postconditions
- The report meets the < 2s P95 @500 SLA and inline detection introduces no measurable latency; no functional state change.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- NFR-1 inline detection complements US-ATT-006 TC-ATT-067 (overtime detected in the clock-out transaction) and US-ATT-002 -- late/early is computed in the same punch transaction. If a tenant exceeds 500 employees, NFR-3 is out of stated scope; the report should still degrade gracefully (note observed behaviour).
