---
id: TC-CHR-ISO-010
user_story: US-CHR-001
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-010: API rejects employee requests without valid tenant context

## 1. Test Objective
Verify that all employee API endpoints reject requests that lack a valid tenant context (no subdomain resolved, no X-Tenant-Subdomain header, or invalid tenant). This ensures the TenantResolutionMiddleware runs before any employee operations.

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-4

## 3. Preconditions
- The API is running and accessible.
- A valid JWT token exists but no tenant subdomain is provided.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | (none) | No tenant context |
| Invalid subdomain | nonexistent.yourhrm.com | Unknown tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/employees` with a valid JWT but no subdomain/tenant header | Request is rejected. Response is 404 (unknown tenant) or 400 (missing tenant context). |
| 2 | Send `POST /api/v1/tenant/employees` with a valid JWT but from "nonexistent.yourhrm.com" | 404 Not Found (tenant does not exist). |
| 3 | Send `GET /api/v1/tenant/employees` with no JWT and no subdomain | 401 Unauthorized (authentication checked first or simultaneously). |
| 4 | Verify no employee data is returned in any of the error responses | Error responses contain only the error message, no data. |

## 6. Postconditions
- Employee API endpoints are inaccessible without a valid tenant context.
- No employee data is leaked in error responses.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
