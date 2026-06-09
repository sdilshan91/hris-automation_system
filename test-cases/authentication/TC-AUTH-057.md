---
id: TC-AUTH-057
user_story: US-AUTH-007
module: Authentication
priority: high
type: performance
status: draft
created: 2026-06-09
---

# TC-AUTH-057: Redis outage falls back to PostgreSQL without failing tenant requests

## 1. Test Objective
Verify that Redis unavailability does not fail tenant resolution for provisioned tenants and that the system records degraded cache behavior.

## 2. Related Requirements
- User Story: US-AUTH-007
- Acceptance Criteria: AC-1, AC-6
- Functional Requirements: FR-5, FR-6
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant `acme` exists with active status in PostgreSQL.
- Redis can be stopped, blocked, or replaced by a fault-injected cache client for the test.
- PostgreSQL is reachable.
- Application logging and health metrics are enabled.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Host | acme.yourhrm.com | Active provisioned tenant |
| Redis state | Unavailable | Connection refused, timeout, or injected exception |
| Expected route result | Request proceeds | Tenant resolved from PostgreSQL |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Simulate Redis unavailability. | Cache client fails predictably without bringing down the API process. |
| 2 | Send a request to `https://acme.yourhrm.com/api/v1/auth/login` or another tenant route. | Middleware catches Redis failure and queries PostgreSQL. |
| 3 | Verify response behavior. | Request proceeds to the target route with a populated acme `ITenantContext`; no HTTP 500 is returned because of Redis. |
| 4 | Verify logs and metrics. | Warning or degraded-cache metric is emitted with no secrets and no tenant data leakage. |
| 5 | Send a request to `https://unknown.yourhrm.com/` while Redis remains unavailable. | PostgreSQL lookup returns no tenant and the safe static 404 response is returned. |
| 6 | Restore Redis and send another acme request. | Tenant resolution resumes normal cache behavior and may repopulate the cache. |

## 6. Postconditions
- Tenant requests remain available during Redis outage when PostgreSQL is healthy.
- Redis outage is observable in logs or metrics.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
