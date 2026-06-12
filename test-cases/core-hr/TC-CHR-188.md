---
id: TC-CHR-188
user_story: US-CHR-007
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-188: Location API response times within SLA (read <= 400ms P95, write <= 800ms P95)

## 1. Test Objective
Verify that location CRUD API endpoints meet the performance SLA defined in NFR-1: read operations respond within 400ms at the 95th percentile, and write operations respond within 800ms at the 95th percentile.

## 2. Related Requirements
- User Story: US-CHR-007
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Tenant "acme" has 50 locations (to provide a realistic data volume).
- Test environment is representative of production (not throttled beyond normal conditions).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Location Count | 50 | Realistic dataset |
| Test Iterations | 20 | For P95 calculation |
| Read SLA | <= 400ms P95 | NFR-1 |
| Write SLA | <= 800ms P95 | NFR-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/locations` 20 times, recording response times | All responses return 200 OK with the list of 50 locations. |
| 2 | Calculate P95 for the GET list endpoint | Sort 20 measurements; 19th value (P95) must be <= 400ms. |
| 3 | Send `GET /api/v1/tenant/locations/{id}` for a specific location 20 times | All responses return 200 OK with the single location. |
| 4 | Calculate P95 for the GET single endpoint | P95 must be <= 400ms. |
| 5 | Send `POST /api/v1/tenant/locations` with a valid body 20 times (unique names each time) | All responses return 201 Created. |
| 6 | Calculate P95 for the POST endpoint | P95 must be <= 800ms. |
| 7 | Send `PUT /api/v1/tenant/locations/{id}` with a valid body 20 times | All responses return 200 OK. |
| 8 | Calculate P95 for the PUT endpoint | P95 must be <= 800ms. |

## 6. Postconditions
- Performance data is recorded for analysis.
- All test locations created during performance testing can be cleaned up.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
