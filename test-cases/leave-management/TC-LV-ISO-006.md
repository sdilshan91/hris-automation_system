---
id: TC-LV-ISO-006
user_story: US-LV-002
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-006: API rejects entitlement requests without valid tenant context

## 1. Test Objective
Verify that the entitlement rule and override API endpoints reject requests that lack a valid tenant context. Without tenant resolution (no subdomain, no `X-Tenant-Subdomain` header), the API must not return any entitlement data and must respond with an appropriate error.

## 2. Related Requirements
- User Story: US-LV-002
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with entitlement rules configured.
- API accessible without subdomain-based tenant resolution.

## 4. Test Data
| Scenario | Subdomain/Header | Expected |
|----------|-----------------|----------|
| No tenant header | None | 400 or 403 |
| Invalid subdomain | nonexistent.yourhrm.com | 404 or 400 |
| Empty X-Tenant-Subdomain | X-Tenant-Subdomain: "" | 400 or 403 |
| Reserved subdomain | admin.yourhrm.com | System context; entitlement API not available |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/leave-entitlement-rules` with a valid JWT but NO `X-Tenant-Subdomain` header and no subdomain | Response is 400 Bad Request or 403 Forbidden: "Tenant context is required." |
| 2 | Send `GET /api/v1/leave-entitlement-rules` with `X-Tenant-Subdomain: nonexistent` | Response is 404 Not Found or 400 Bad Request: "Tenant not found." |
| 3 | Send `GET /api/v1/leave-entitlement-rules` with `X-Tenant-Subdomain: ""` (empty) | Response is 400 Bad Request: "Invalid tenant context." |
| 4 | Send `POST /api/v1/leave-entitlement-rules` with valid body but no tenant context | Response is 400 Bad Request or 403 Forbidden. No rule created. |
| 5 | Verify that no entitlement data is returned in any error response body | Error responses contain only error message, no entitlement rule data. |
| 6 | Verify database: no records with NULL tenant_id exist in `leave_entitlement_rule` | All records have a valid tenant_id. |

## 6. Postconditions
- No entitlement operations succeed without a valid tenant context.
- TenantResolutionMiddleware correctly blocks requests without tenant identification.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
