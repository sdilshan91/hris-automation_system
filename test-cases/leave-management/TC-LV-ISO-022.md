---
id: TC-LV-ISO-022
user_story: US-LV-006
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-022: API rejects balance/ledger/upcoming requests without a valid tenant context

## 1. Test Objective
Verify that the dashboard data endpoints require a resolved tenant context: a request whose subdomain/tenant cannot be resolved (or whose tenant context is missing) does not return another tenant's data and is rejected/empty rather than defaulting to a global scope (NFR-3, US-AUTH-007).

## 2. Related Requirements
- User Story: US-LV-006
- Non-Functional Requirements: NFR-3
- Related: US-AUTH-007 (tenant resolution)

## 3. Preconditions
- Tenant "acme" active with employee data. Tenant resolution runs from the subdomain / `X-Tenant-Subdomain` header (dev).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Missing tenant | no subdomain / unknown subdomain | -- |
| Mismatched tenant | token for acme + globex subdomain | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `my-balance` with a valid token but no resolvable tenant context | Request is rejected (401/400) or returns no data; it never falls back to an unscoped/global balance query. |
| 2 | Call `my-ledger`/`my-upcoming` with an unknown/reserved subdomain | Tenant does not resolve; no leave data is returned for any tenant. |
| 3 | Call with an acme token but a globex tenant context (mismatch) | The mismatch is rejected; no globex data and no acme data is served under the wrong context. |
| 4 | Confirm logs/audit | The rejected attempt is handled safely with no cross-tenant data exposure. |

## 6. Postconditions
- Endpoints require a valid, matching tenant context; no unscoped access.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
