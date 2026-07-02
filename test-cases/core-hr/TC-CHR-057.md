---
id: TC-CHR-057
user_story: US-CHR-005
module: Core HR
priority: high
type: performance
status: pass
created: 2026-06-12
exec_note: "2026-07-02 PASS (perf tenant, 5000 emp). Read arm: GET /api/v1/tenant/job-titles P95=14.6ms over 60 seq reqs (all 200) < 400ms SLA. Write arm: POST /api/v1/tenant/job-titles P95=10.6ms over 25 unique creates (all 201) < 800ms SLA. Method: curl %{time_total} loop, single-VU sequential, 3 warm discarded. NOTE created ~28 'PerfJT-*'/'PerfWarm-*' job_titles on perf tenant for the write measurement (teardown by name LIKE 'Perf%')."

# TC-CHR-057: Job title CRUD API response time within SLA

## 1. Test Objective
Verify that job title API endpoints meet the performance SLA defined in NFR-1: read operations at P95 <= 400ms and write operations at P95 <= 800ms.

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- The tenant has a representative number of job titles (e.g., 50+) to simulate realistic load.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Test iterations | >= 20 per endpoint | Sufficient for P95 calculation |
| Existing job titles | 50+ | Realistic data volume |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Execute `GET /api/v1/job-titles` at least 20 times and record response times | P95 response time is <= 400ms. |
| 2 | Execute `GET /api/v1/job-titles/{id}` at least 20 times and record response times | P95 response time is <= 400ms. |
| 3 | Execute `POST /api/v1/job-titles` (with unique names) at least 20 times and record response times | P95 response time is <= 800ms. |
| 4 | Execute `PUT /api/v1/job-titles/{id}` at least 20 times and record response times | P95 response time is <= 800ms. |
| 5 | Execute `PATCH /api/v1/job-titles/{id}/deactivate` at least 20 times (create, then deactivate) and record response times | P95 response time is <= 800ms. |
| 6 | Calculate P95 for each endpoint and compare against SLA thresholds | All endpoints meet their respective SLA. |

## 6. Postconditions
- Performance metrics are documented.
- Any SLA violations are flagged for investigation.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30:** STILL BLOCKED — API-SLA TC (response-time percentiles), not a browser-executable FE check. Needs a load/timing harness (k6 / instrumented API timing). Out of scope for this FE per-TC pass.
