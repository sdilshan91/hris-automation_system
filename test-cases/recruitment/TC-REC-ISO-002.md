---
id: TC-REC-ISO-002
user_story: US-REC-001
module: Recruitment
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-REC-ISO-002: API rejects vacancy requests without a valid/resolvable tenant context

## 1. Test Objective
Verify that authenticated vacancy API calls made without a valid, resolvable tenant context are rejected rather than served against an arbitrary or default tenant. Tenant resolution (subdomain / `X-Tenant-Subdomain`) must produce a concrete tenant before any vacancy read/write proceeds.

## 2. Related Requirements
- User Story: US-REC-001
- Acceptance Criteria: AC-4
- Non-Functional Requirements: NFR-2
- Constraints: tenant resolution precedes data access (TenantResolutionMiddleware)

## 3. Preconditions
- Tenant "acme" exists and is active with vacancies.
- A valid acme JWT is available for the authenticated cases.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Missing context | no subdomain + no X-Tenant-Subdomain | Should not resolve a tenant |
| Unknown tenant | X-Tenant-Subdomain: doesnotexist | Unresolvable |
| Inactive/reserved | suspended or reserved subdomain | Must not serve tenant data |
| Mismatched | acme JWT + X-Tenant-Subdomain: globex | Token/context mismatch |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/recruitment/vacancies` with a valid token but no resolvable tenant (no subdomain, no `X-Tenant-Subdomain`) | Request is rejected (400/401) -- no vacancies returned against a default/arbitrary tenant. |
| 2 | Call the same with `X-Tenant-Subdomain: doesnotexist` | Rejected (tenant not resolvable); no data served. |
| 3 | Call with a suspended/reserved subdomain | Rejected; no tenant vacancy data served. |
| 4 | Call with a valid acme JWT but `X-Tenant-Subdomain: globex` (mismatch) | Rejected (403/401) -- the request is not served against globex; no cross-tenant access via header override. |
| 5 | Attempt `POST /api/v1/recruitment/vacancies` (create) with no resolvable tenant | Rejected; no `vacancy` row created (TenantInterceptor has no tenant to stamp). |

## 6. Postconditions
- No vacancy data is read or written without a valid resolved tenant context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
