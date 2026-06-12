---
id: TC-CHR-ISO-034
user_story: US-CHR-009
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-034: API rejects status change requests without valid tenant context

## 1. Test Objective
Verify that the status change API endpoint rejects requests that lack a valid tenant context (no subdomain, invalid subdomain, or missing tenant resolution). This validates NFR-2 and the TenantResolutionMiddleware enforcement.

## 2. Related Requirements
- User Story: US-CHR-009
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Employee "John Smith" (`emp-001-uuid`) exists in tenant "acme".
- An HR Officer user exists and has a valid JWT token issued for tenant "acme".

## 4. Test Data
| Scenario | Header/Subdomain | Notes |
|----------|-----------------|-------|
| No tenant header | No X-Tenant-Subdomain header, no subdomain | Missing tenant context |
| Invalid subdomain | X-Tenant-Subdomain: nonexistent | Tenant does not exist |
| Empty subdomain | X-Tenant-Subdomain: (empty) | Empty value |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/tenant/employees/emp-001-uuid/status` with valid body but NO `X-Tenant-Subdomain` header and not through any tenant subdomain. | Response is 400 Bad Request or 401/403 (tenant context required). Employee status unchanged. |
| 2 | Send the same request with `X-Tenant-Subdomain: nonexistent`. | Response is 400 or 404 (tenant not found). Employee status unchanged. |
| 3 | Send the same request with `X-Tenant-Subdomain: ""` (empty string). | Response is 400 (invalid tenant context). Employee status unchanged. |
| 4 | Verify no employment history entries were created in any tenant. | No records created. |

## 6. Postconditions
- No status changes occurred.
- No data was written to any tenant's tables.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
