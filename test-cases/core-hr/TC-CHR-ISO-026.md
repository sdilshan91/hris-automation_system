---
id: TC-CHR-ISO-026
user_story: US-CHR-007
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-026: API rejects location requests without valid tenant context

## 1. Test Objective
Verify that the location CRUD API endpoints reject requests that do not have a valid tenant context resolved (e.g., missing subdomain, unknown subdomain, or no X-Tenant-Subdomain header in dev mode). The system must not return location data without knowing which tenant is being accessed. This validates FR-8 and NFR-2.

## 2. Related Requirements
- User Story: US-CHR-007
- Functional Requirements: FR-8
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with at least one location.
- A valid JWT token exists for a user in tenant "acme".
- The request is sent without a valid subdomain or tenant header.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Valid JWT | Token for acme user | Authentication is valid |
| Subdomain | (missing or invalid) | No tenant resolution |
| X-Tenant-Subdomain Header | (absent) | Dev mode header missing |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/locations` with a valid JWT but from an unknown subdomain (e.g., `unknown.yourhrm.com`) | Response returns 404 (tenant not found) or 400 (invalid tenant context). No location data returned. |
| 2 | Send `GET /api/v1/tenant/locations` with a valid JWT but no subdomain resolution (direct IP or bare domain) | TenantResolutionMiddleware blocks the request. Response indicates tenant context is required. |
| 3 | Send `POST /api/v1/tenant/locations` with a valid body but from an unknown subdomain | Request is rejected before reaching the controller. No location is created. |
| 4 | Send `GET /api/v1/tenant/locations` with a valid JWT from acme but with a tampered `X-Tenant-Subdomain: globex` header | The middleware resolves the subdomain from the actual request host, not from a user-provided header that doesn't match. If the host is acme, only acme data is returned regardless of the header. |
| 5 | Verify that no location data from any tenant is leaked in error responses | Error responses contain generic messages, not location details or tenant IDs. |

## 6. Postconditions
- No location data was returned for invalid tenant contexts.
- No cross-tenant data exposure occurred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
