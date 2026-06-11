---
id: TC-CHR-026
user_story: US-CHR-004
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-11
---

# TC-CHR-026: Department CRUD API response time within SLA

## 1. Test Objective
Verify that department API endpoints meet the performance SLA defined in NFR-1: read operations P95 <= 400ms, write operations P95 <= 800ms.

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- The tenant has at least 100 departments to simulate a realistic dataset.
- System is under normal load (not stress testing).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department Count | 100+ | Pre-seeded departments |
| Test Iterations | 100 per endpoint | For P95 calculation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Execute 100 requests to `GET /api/v1/departments` (list all) and record response times | P95 response time <= 400ms. |
| 2 | Execute 100 requests to `GET /api/v1/departments/{id}` (single department) and record response times | P95 response time <= 400ms. |
| 3 | Execute 100 requests to `GET /api/v1/departments/tree` (hierarchy tree) and record response times | P95 response time <= 400ms. |
| 4 | Execute 100 requests to `POST /api/v1/departments` (create) with unique names and record response times | P95 response time <= 800ms. |
| 5 | Execute 100 requests to `PUT /api/v1/departments/{id}` (update) and record response times | P95 response time <= 800ms. |
| 6 | Calculate and report P50, P95, P99 for each endpoint | All P95 values within SLA limits. |

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
