---
id: TC-CHR-ISO-022
user_story: US-CHR-006
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-022: API rejects org-tree requests without valid tenant context

## 1. Test Objective
Verify that the org-tree API endpoint rejects requests that lack a valid tenant context (no subdomain resolution, missing tenant header, or invalid tenant). This ensures that the `TenantResolutionMiddleware` is enforced before the org-tree endpoint is reached. This validates FR-8, NFR-3.

## 2. Related Requirements
- User Story: US-CHR-006
- Functional Requirements: FR-8
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- The org-tree API endpoint is deployed.
- An authenticated user exists but the request omits or corrupts the tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | GET /api/v1/org-tree?view=department&depth=2 | Org tree API |
| Scenario 1 | No X-Tenant-Subdomain header and no subdomain | Missing tenant |
| Scenario 2 | X-Tenant-Subdomain: nonexistent-tenant | Invalid tenant |
| Scenario 3 | X-Tenant-Subdomain: (empty string) | Empty tenant header |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/org-tree?view=department&depth=2` with valid JWT but no tenant subdomain (direct IP or localhost without subdomain) | Response status is 404 (tenant not found) or 400 (tenant context required). No org-tree data is returned. |
| 2 | Send the same request with `X-Tenant-Subdomain: nonexistent-tenant` | Response status is 404. The response body does not contain any department or employee data. |
| 3 | Send the same request with `X-Tenant-Subdomain: ""` (empty string) | Response status is 400 or 404. No data is returned. |
| 4 | Send the same request with `X-Tenant-Subdomain: admin` (reserved subdomain) using a non-system-admin JWT | The request is either redirected to the admin context (if the user is a system admin) or rejected with 403 (if not a system admin). No tenant org-tree data is returned. |
| 5 | Verify no org-tree data is leaked in any error response | Error response bodies contain only generic error messages, no department names, employee names, or node IDs. |

## 6. Postconditions
- No data was exposed without proper tenant context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
