---
id: TC-AUTH-ISO-003
user_story: US-AUTH-002, US-AUTH-007
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-ISO-003: API rejects requests with mismatched tenant context

## 1. Test Objective
Verify that the API rejects any request where the JWT's `tenant_id` claim does not match the resolved tenant from the subdomain, preventing cross-tenant data access through token manipulation or replay attacks.

## 2. Related Requirements
- User Story: US-AUTH-002 (BR-1)
- User Story: US-AUTH-007 (FR-1, FR-6)
- User Story: US-AUTH-006 (FR-5, FR-10)

## 3. Preconditions
- User `multi@acme.com` has active memberships in Tenant A ("acme") and Tenant B ("globex").
- User is authenticated in Tenant A with a valid JWT containing `tenant_id` = acme UUID.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | multi@acme.com | Multi-tenant user |
| JWT tenant_id | acme UUID | Issued for acme |
| Request target | globex.yourhrm.com | Different tenant subdomain |
| API endpoint | GET /api/v1/tenant/users | Any tenant-scoped endpoint |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate at `acme.yourhrm.com` and obtain a valid JWT | JWT has `tenant_id` = acme UUID. |
| 2 | Send a request to `globex.yourhrm.com/api/v1/tenant/users` using the acme JWT | HTTP 401 or 403; the system detects `tenant_id` in JWT (acme) does not match the resolved tenant (globex). |
| 3 | Verify no globex data is returned | Response contains no tenant user data from globex. |
| 4 | Attempt to send `POST /api/v1/auth/refresh` to `globex.yourhrm.com` with acme's refresh token cookie | HTTP 401; refresh token is bound to acme tenant_id and cannot be used for globex. |
| 5 | Craft a modified JWT (tampered `tenant_id` to globex UUID) and send to `globex.yourhrm.com` | HTTP 401; JWT signature validation fails because the payload has been modified. |
| 6 | Verify EF Core global query filters enforce tenant scoping at the database level | Even if middleware is bypassed, RLS and query filters prevent cross-tenant data access. |
| 7 | Verify Redis cache keys are tenant-scoped | Cache key pattern `t:{tenantId}:...` ensures no cross-tenant cache poisoning. |
| 8 | Verify the mismatch is logged as a security event | Audit log entry with user_id, JWT tenant_id, resolved tenant_id, IP address. |
| 9 | Repeat with different API endpoints (POST, PUT, DELETE) | All endpoints reject mismatched tenant context consistently. |

## 6. Postconditions
- No cross-tenant data access is possible through token manipulation.
- Security events are logged for all mismatch attempts.
- The system maintains strict tenant isolation at middleware, application, and database layers.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
