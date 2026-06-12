---
id: TC-CHR-095
user_story: US-CHR-001
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-095: Employee creation API response time within SLA (NFR-1)

## 1. Test Objective
Verify that the employee creation API (`POST /api/v1/tenant/employees`) responds within the SLA of <= 800 ms at P95, per NFR-1.

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated.
- The system is running under normal load conditions.
- At least 100 employees already exist in the tenant (realistic data volume).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Number of test requests | 100 | For P95 calculation |
| Payload | Valid employee creation request | All mandatory fields |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send 100 sequential `POST /api/v1/tenant/employees` requests with unique emails | All requests complete. |
| 2 | Record the response time for each request | Times collected. |
| 3 | Calculate the P95 response time | P95 response time is <= 800 ms. |
| 4 | Verify no individual request exceeds 2000 ms (hard timeout) | No timeouts. |
| 5 | Also test `GET /api/v1/tenant/employees` (list) with 200+ employees | P95 response time is <= 400 ms (read SLA). |

## 6. Postconditions
- Performance SLA is met for both write and read operations.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
