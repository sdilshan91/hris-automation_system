---
id: TC-LV-ISO-026
user_story: US-LV-007
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-026: API rejects holiday requests without a valid tenant context

## 1. Test Objective
Verify that holiday endpoints require a resolved tenant context: a request whose subdomain/header resolves to no tenant (or to a different tenant than the authenticated user) is rejected and never reads or writes another tenant's holidays (NFR-2, US-AUTH-007).

## 2. Related Requirements
- User Story: US-LV-007
- Non-Functional Requirements: NFR-2
- Cross-reference: US-AUTH-007 (tenant resolution middleware)

## 3. Preconditions
- Tenant "acme" active with holidays; valid user token for "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Missing tenant | no subdomain / no X-Tenant-Subdomain | unresolved |
| Mismatched tenant | token for acme, context globex | mismatch |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/holidays` with no resolvable tenant context | Request is rejected (401/400 per resolution middleware); no holiday data returned. |
| 2 | Call POST/import with an unresolved tenant context | Rejected; no row is created or stamped to an arbitrary tenant. |
| 3 | Present an "acme" token while the context resolves to "globex" | The mismatch is rejected; no cross-tenant read/write occurs. |
| 4 | Confirm writes are tenant-stamped from context, not the body | A create never trusts a tenant id from the request body; TenantInterceptor stamps from the resolved context. |

## 6. Postconditions
- Holiday endpoints are inaccessible without a valid, matching tenant context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
