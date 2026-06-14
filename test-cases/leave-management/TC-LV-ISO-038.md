---
id: TC-LV-ISO-038
user_story: US-LV-010
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-038: API rejects a cancellation request without a valid tenant context (NFR-2)

## 1. Test Objective
Verify that the cancellation endpoint cannot be invoked without a resolved tenant context: a request whose subdomain/`X-Tenant-Subdomain` is missing, unknown, or for an inactive tenant is rejected before any cancellation, ledger write, or notification occurs (NFR-2).

## 2. Related Requirements
- User Story: US-LV-010
- Non-Functional Requirements: NFR-2
- Architecture: TenantResolutionMiddleware (runs before auth)

## 3. Preconditions
- Tenant "acme" is active with a valid PENDING request R.
- A known-unknown subdomain (`nope`) and an inactive tenant subdomain are available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Missing tenant | no subdomain / no header | unresolved |
| Unknown tenant | `nope.yourhrm.com` | not found |
| Inactive tenant | suspended tenant subdomain | resolution refused |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST /api/v1/leaves/{R}/cancel` with NO tenant subdomain/header | Rejected (no tenant context resolved); no cancellation applied. |
| 2 | Repeat with an unknown subdomain `nope` | Rejected -- tenant not found; request not processed. |
| 3 | Repeat with an inactive/suspended tenant subdomain | Rejected -- resolution refuses inactive tenants. |
| 4 | Inspect R (under acme) | Unchanged -- still Pending; no ledger row, no audit, no notification. |

## 6. Postconditions
- Cancellation requires a valid, active tenant context; R is untouched by context-less requests.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
