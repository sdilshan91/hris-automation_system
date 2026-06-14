---
id: TC-LV-ISO-046
user_story: US-LV-012
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-046: API rejects report/analytics requests without a valid tenant context (NFR-3)

## 1. Test Objective
Verify that the report, analytics, and export endpoints require a resolved tenant context (subdomain / `X-Tenant-Subdomain`); a request that cannot resolve to a tenant does not return any tenant's report data.

## 2. Related Requirements
- User Story: US-LV-012
- Non-Functional Requirements: NFR-3
- Cross-ref: TenantResolutionMiddleware, ITenantContext

## 3. Preconditions
- Report data exists for tenant "acme".
- Ability to call the API with an unresolved/missing tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant context | none / reserved / unknown subdomain | unresolved |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/reports/balance-summary` with no resolvable tenant (missing/blank `X-Tenant-Subdomain`, non-tenant host) | The request is rejected / returns no tenant data — the global query filter has no tenant and yields nothing; no cross-tenant fallback. |
| 2 | Call an analytics endpoint with an unknown subdomain | No data returned for any real tenant. |
| 3 | Call the export endpoint with an unresolved tenant | No file generated for any tenant's data. |
| 4 | Repeat with a valid acme context (positive control) | The endpoints resolve acme and return acme data — confirms the rejection was due to missing tenant context. |

## 6. Postconditions
- Report/analytics/export endpoints require a valid tenant context; unresolved requests disclose no tenant data.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
