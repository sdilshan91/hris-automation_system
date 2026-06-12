---
id: TC-CHR-ISO-002
user_story: US-CHR-004
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-CHR-ISO-002: API rejects department requests without valid tenant context

## 1. Test Objective
Verify that department API endpoints reject requests that lack a valid tenant context (no subdomain resolved, no X-Tenant-Subdomain header, or invalid tenant), preventing unscoped data access.

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-2
- Preconditions: Section 2 (tenant context resolved from subdomain)

## 3. Preconditions
- The API is running and accessible.
- No valid tenant subdomain is provided in the request.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | (none) | No tenant context |
| Invalid Subdomain | nonexistent.yourhrm.com | Unknown tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/departments` with no subdomain or X-Tenant-Subdomain header (direct IP/host access) | Response is 400 Bad Request or 404 Not Found (tenant resolution middleware rejects the request before reaching the controller). |
| 2 | Send `GET /api/v1/departments` with `X-Tenant-Subdomain: nonexistent` | Response is 404 Not Found (unknown tenant). |
| 3 | Send `POST /api/v1/departments` with valid auth but no tenant context | Response is 400 or 404. No department is created. |
| 4 | Verify that no department data is returned in any error response | Error responses do not leak department data or schema information. |

## 6. Postconditions
- No data was returned or modified without valid tenant context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
