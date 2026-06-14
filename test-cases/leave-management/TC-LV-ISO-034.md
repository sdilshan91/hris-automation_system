---
id: TC-LV-ISO-034
user_story: US-LV-009
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-034: API rejects team-calendar requests without a valid tenant context (NFR-2)

## 1. Test Objective
Verify the team-calendar endpoint requires a resolvable tenant context (subdomain / X-Tenant-Subdomain) and does not fall back to an unscoped, all-tenant query when the tenant cannot be resolved.

## 2. Related Requirements
- User Story: US-LV-009
- Non-Functional Requirements: NFR-2
- Cross-reference: TenantResolutionMiddleware, US-AUTH-007

## 3. Preconditions
- Tenant "acme" with calendar data; an authenticated token but a request crafted without a valid tenant subdomain/header.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Missing tenant | no X-Tenant-Subdomain / unknown subdomain | resolution fails |
| Mismatched | token tenant != request subdomain | rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call the team-calendar API with no resolvable tenant context | The request is rejected (401/400) or returns empty; it does NOT return data across all tenants. |
| 2 | Call with an unknown/reserved subdomain | Tenant resolution fails closed; no calendar data is leaked. |
| 3 | Call with a token whose tenant differs from the resolved subdomain | The mismatch is rejected; no cross-tenant data served. |
| 4 | Confirm no unscoped query path | With no tenant resolved, the EF global query filter yields no rows (fail-closed), not an unfiltered result set. |

## 6. Postconditions
- The endpoint fails closed without a valid tenant context; no unscoped/all-tenant data is returned.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
