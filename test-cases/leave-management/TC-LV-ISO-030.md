---
id: TC-LV-ISO-030
user_story: US-LV-008
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-030: Preview API rejects requests without a valid tenant context (NFR-2)

## 1. Test Objective
Verify the carry-forward preview API requires a resolved tenant context: a request that arrives without a valid tenant (unresolvable subdomain / missing tenant resolution) is rejected and returns no projection, so carry-forward data can never be read outside a tenant scope (NFR-2, FR-4).

## 2. Related Requirements
- User Story: US-LV-008
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-4, FR-5
- Cross-reference: US-AUTH-007 (tenant resolution middleware)

## 3. Preconditions
- Tenant "acme" exists with carry-forward configuration.
- A way to issue a request whose tenant context does not resolve (e.g. unknown subdomain / absent `X-Tenant-Subdomain` in dev).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Unresolvable tenant | unknown-subdomain | -- |
| Endpoint | GET /api/v1/leaves/carry-forward-preview?year=2026 | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call the preview endpoint with an unresolvable tenant context | The request is rejected (no tenant resolved); no carry-forward projection is returned. |
| 2 | Call with a valid token but mismatched/absent tenant context | Rejected; the response carries no other-tenant data. |
| 3 | Call with a correctly resolved tenant context (control) | 200 OK with that tenant's projection only -- confirming the rejection is specific to missing tenant context. |
| 4 | Verify no leakage | At no point is another tenant's carry-forward/forfeiture data exposed via an unscoped request. |

## 6. Postconditions
- The preview API only serves data within a valid resolved tenant context; unscoped requests are rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
