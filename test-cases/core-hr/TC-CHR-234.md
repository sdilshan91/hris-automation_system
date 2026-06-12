---
id: TC-CHR-234
user_story: US-CHR-009
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-234: Status change API response time within 800ms P95 (NFR-1)

## 1. Test Objective
Verify that the status change API endpoint responds within the SLA of 800ms at the 95th percentile (P95) under normal load. This validates NFR-1.

## 2. Related Requirements
- User Story: US-CHR-009
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- 50 test employees exist with status `active` in tenant "acme".
- The API server is running in a production-like environment (or staging).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Number of requests | 100 | Sequential or low-concurrency |
| SLA | P95 <= 800ms | Write operation SLA |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Prepare 100 status change requests (active -> suspended) targeting distinct employees, each with a unique Idempotency-Key. | Requests are ready. |
| 2 | Execute the 100 requests sequentially (or at low concurrency, e.g., 5 concurrent), recording response times. | All requests return 200 OK. |
| 3 | Calculate the P95 response time from the recorded data. | P95 response time is <= 800ms. |
| 4 | Calculate the median and P99 for informational purposes. | Median is reported. P99 is reported (no hard SLA but noted). |

## 6. Postconditions
- 100 employees now have status `suspended`.
- Performance metrics are recorded for analysis.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
