---
id: TC-LV-ISO-042
user_story: US-LV-011
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-042: API rejects LOP requests without a valid tenant context (NFR-2)

## 1. Test Objective
Verify that LOP endpoints (`assign-lop`, `lop-summary`, compulsory bulk-assign) cannot be executed without a resolved tenant context: a request whose subdomain/`X-Tenant-Subdomain` resolves to no tenant (or is missing) is rejected before any LOP data is read or written (NFR-2).

## 2. Related Requirements
- User Story: US-LV-011
- Non-Functional Requirements: NFR-2
- Cross-ref: TenantResolutionMiddleware (per vault/CLAUDE.md)

## 3. Preconditions
- The API is deployed; tenant "acme" exists.
- A valid authenticated user is available for the positive control.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant context | missing / unknown subdomain | unresolved |
| Reserved subdomain | admin.* | system, not a tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `POST /api/v1/leaves/assign-lop` with no resolvable tenant (missing/unknown subdomain) | Rejected (400/401/403 per the platform's unresolved-tenant handling); no LOP row created; the handler does not run an untenant-scoped query. |
| 2 | Call `GET /api/v1/leaves/lop-summary` with an unknown tenant subdomain | Rejected; no LOP data returned. |
| 3 | Call an LOP endpoint with a reserved/system (admin) subdomain | Tenant-scoped LOP processing does not occur for a non-tenant context. |
| 4 | Repeat with a valid acme tenant context (positive control) | The request resolves the tenant and proceeds — confirms the rejection is tenant-context-based. |

## 6. Postconditions
- LOP endpoints require a resolved tenant context; unresolved-context requests are rejected with no data access.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
