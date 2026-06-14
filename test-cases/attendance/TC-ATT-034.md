---
id: TC-ATT-034
user_story: US-ATT-003
module: Attendance
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-ATT-034: Regularization submission API responds within 500ms at P95 under representative load (NFR-1)

## 1. Test Objective
Verify NFR-1: the regularization submission endpoint's response time stays at or below 500ms at the 95th percentile under representative concurrent load, with the full validation path active (lookback check, duplicate-pending check, payroll-lock check, time-consistency validation, existing-log lookup/link, regularization insert, workflow initiation, audit write) and tenant isolation enforced.

## 2. Related Requirements
- User Story: US-ATT-003
- Non-Functional Requirements: NFR-1
- Functional Requirements: FR-2, FR-3, FR-6, FR-7

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, regularization workflow configured, lookback = 7 days.
- A pool of distinct active employees (e.g., 200), each holding `Attendance.Regularize.Self`, each with a valid recent past date eligible for regularization (within lookback, no existing pending) so each request exercises the create path rather than a reject path.
- Test environment representative of production; a warm-up phase precedes measurement.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | POST /api/v1/attendance/regularizations | Write SLA |
| Concurrent virtual users | 50 (ramped) | Representative |
| Total requests | >= 2,000 | Distinct employees/dates, each a valid create |
| Target | P95 <= 500ms | NFR-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run a warm-up phase (e.g., 200 requests) and discard its measurements | JIT/connection pool warmed; not counted. |
| 2 | Execute the load profile (ramp to 50 concurrent users, >= 2,000 valid submissions) | All requests complete; latency recorded per request. |
| 3 | Compute P50, P95, P99 | P95 <= 500ms (NFR-1). Report P50/P99 for context. |
| 4 | Verify correctness under load | Each submission creates exactly one PENDING regularization with correct tenant/employee scope; no duplicate-pending violations; no cross-request data bleed; error rate ~0%. |
| 5 | Verify the validation + workflow-init path is not a bottleneck | The lookback/duplicate/payroll-lock checks and workflow initiation stay within the budget; if the workflow engine or payroll-lock service is a seam/stub, record which dependencies were live vs stubbed and re-measure once they land. |

## 6. Postconditions
- Latency percentiles recorded; pass/fail against the 500ms P95 documented.
- One correctly scoped PENDING regularization per submission; no integrity violations under load.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
