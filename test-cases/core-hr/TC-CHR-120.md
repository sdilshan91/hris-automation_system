---
id: TC-CHR-120
user_story: US-CHR-002
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-120: Employee profile API read response time within 400ms P95 (NFR-2)

## 1. Test Objective
Verify that the `GET /api/v1/tenant/employees/{id}` API endpoint responds within 400ms at the 95th percentile under typical load. This validates NFR-2.

## 2. Related Requirements
- User Story: US-CHR-002
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active` and at least 100 employees.
- HR Officer is authenticated in "acme" tenant.
- API server is running under normal conditions (no artificial load or resource constraints).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Employee IDs | 5 different employee UUIDs | Varied data sizes |
| Iterations per ID | 20 | 100 total requests |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Select 5 employees with varying profile completeness (minimal data, average data, fully populated, many emergency contacts, many custom fields) | Employee IDs noted. |
| 2 | For each employee ID, send `GET /api/v1/tenant/employees/{id}` 20 times and record the response time (from request sent to response received) | All 100 response times recorded. |
| 3 | Calculate P95 across all 100 measurements | P95 response time must be <= 400ms. |
| 4 | Verify all responses are 200 OK with correct data | No errors or timeouts in the measurement set. |
| 5 | Also measure the PATCH endpoint: send 20 PATCH requests on a test employee | P95 for write operations must be <= 800ms. |

## 6. Postconditions
- Performance measurements recorded and documented.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
