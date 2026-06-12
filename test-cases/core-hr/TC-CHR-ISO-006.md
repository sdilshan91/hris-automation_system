---
id: TC-CHR-ISO-006
user_story: US-CHR-005
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-006: API rejects job title requests without valid tenant context

## 1. Test Objective
Verify that the job title API endpoints reject requests that do not have a valid tenant context (i.e., the `TenantResolutionMiddleware` has not resolved a tenant). This ensures that no job title data can be accessed or modified without proper tenant scoping.

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- A valid JWT exists but no tenant context is provided (no subdomain, no `X-Tenant-Subdomain` header, or invalid subdomain).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain (case 1) | (none) | No subdomain in host header |
| Subdomain (case 2) | unknown-tenant | Non-existent tenant |
| Auth | Valid JWT | Authenticated but no tenant context |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/job-titles` with a valid JWT but no subdomain/tenant header | Response status is 400 Bad Request or 404 Not Found (tenant resolution fails). No job title data is returned. |
| 2 | Call `GET /api/v1/job-titles` with subdomain set to "unknown-tenant" | Response status is 404 Not Found (unknown tenant). No data returned. |
| 3 | Call `POST /api/v1/job-titles` with valid body but no tenant context | Response status is 400 or 404. No record is created. |
| 4 | Call `PUT /api/v1/job-titles/{any_id}` with no tenant context | Response status is 400 or 404. No record is modified. |
| 5 | Verify the database has no records with `tenant_id = null` in the `job_titles` table | All records have a valid `tenant_id`. |

## 6. Postconditions
- No job title data was accessed or created without a valid tenant context.
- The tenant resolution middleware correctly blocks contextless requests.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
