---
id: TC-CHR-ISO-014
user_story: US-CHR-002
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-014: API rejects employee profile requests without valid tenant context

## 1. Test Objective
Verify that all employee profile API endpoints reject requests that do not have a valid tenant context resolved (no subdomain, missing/invalid X-Tenant-Subdomain header). This validates NFR-3 and the TenantResolutionMiddleware requirement.

## 2. Related Requirements
- User Story: US-CHR-002
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-7

## 3. Preconditions
- The API server is running.
- Employee records exist in various tenants.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee ID | {any_employee_id} | Any valid UUID |
| Subdomain | (none) | No tenant context |
| Invalid Subdomain | nonexistent-tenant | Unknown tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/employees/{any_employee_id}` with no X-Tenant-Subdomain header and no subdomain in the host | Response is 400 or 404 (tenant not resolved). No employee data returned. |
| 2 | Send `GET /api/v1/tenant/employees` with `X-Tenant-Subdomain: nonexistent-tenant` | Response is 404 (tenant not found). |
| 3 | Send `PATCH /api/v1/tenant/employees/{any_employee_id}` with empty `X-Tenant-Subdomain: ""` header | Response is 400 or 404. No modification occurs. |
| 4 | Send `GET /api/v1/tenant/employees` with a valid JWT but no tenant context | Response is 400 or 404. The authenticated user alone is insufficient -- tenant resolution must occur. |

## 6. Postconditions
- No data returned or modified without valid tenant context.
- TenantResolutionMiddleware correctly blocks unresolvable requests.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
