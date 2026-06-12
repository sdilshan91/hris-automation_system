---
id: TC-CHR-289
user_story: US-CHR-011
module: Core HR
priority: critical
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-289: Manager assignment API response time within 800ms P95 including cycle detection

## 1. Test Objective
Verify that the manager assignment API (including server-side cycle detection) responds within 800 ms at the 95th percentile. This validates NFR-1.

## 2. Related Requirements
- User Story: US-CHR-011
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with a realistic dataset: 200+ employees with varying hierarchy depths.
- An HR Officer user is authenticated.
- The environment is representative of production-like load (application server warmed up).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Total employees | 200+ | Varying hierarchy depths |
| Test iterations | 50 | For P95 calculation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Prepare 50 valid manager assignment requests (different employee/manager pairs, no cycles). | Requests prepared. |
| 2 | Execute all 50 assignments sequentially, recording the response time for each. | All assignments succeed (200 OK). |
| 3 | Calculate the 95th percentile response time from the 50 measurements. | P95 response time is <= 800 ms. |
| 4 | Calculate the average response time. | For informational purposes; P95 is the SLA metric. |

## 6. Postconditions
- 50 manager assignments completed. Performance metrics recorded.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
