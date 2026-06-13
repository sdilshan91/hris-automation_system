---
id: TC-LV-ISO-002
user_story: US-LV-001
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-002: API rejects leave type requests without valid tenant context

## 1. Test Objective
Verify that the leave types API rejects all requests that lack a valid tenant context (no subdomain resolution, missing tenant header, or invalid tenant). The TenantResolutionMiddleware must block these requests before they reach the controller.

## 2. Related Requirements
- User Story: US-LV-001
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Leave types API is deployed.
- A valid JWT token exists but the request is sent without tenant context.

## 4. Test Data
| Scenario | Subdomain / Header | Expected |
|----------|--------------------|----------|
| No subdomain | (none) | 400 Bad Request or redirect |
| Invalid subdomain | nonexistent.yourhrm.com | 404 Tenant Not Found |
| Missing X-Tenant-Subdomain header (dev) | (omitted) | 400 or tenant resolution failure |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/leave-types` with valid JWT but without any tenant context (no subdomain, no X-Tenant-Subdomain header) | Request is rejected. Response 400 Bad Request: "Tenant context is required." No data returned. |
| 2 | Send `GET /api/v1/leave-types` with subdomain `nonexistent.yourhrm.com` | Request is rejected. Response 404: "Tenant not found." |
| 3 | Send `POST /api/v1/leave-types` with valid JWT and valid body but no tenant context | Request is rejected. No leave type created. |
| 4 | Send `GET /api/v1/leave-types` with `X-Tenant-Subdomain: ` (empty value) | Request is rejected with appropriate error. |
| 5 | Verify that TenantResolutionMiddleware logged the failed resolution attempts | Log entries show tenant resolution failures with request details (no sensitive data leaked). |

## 6. Postconditions
- No data was returned or created without valid tenant context.
- All rejections occurred at the middleware level, before controller logic.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
