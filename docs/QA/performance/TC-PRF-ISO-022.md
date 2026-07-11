---
id: TC-PRF-ISO-022
user_story: US-PRF-006
module: Performance Management
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-PRF-ISO-022: Sign-off APIs reject missing/invalid/mismatched tenant context + cross-tenant IDOR (sign/dispute another tenant's review) (NFR-2)

## 1. Test Objective
Verify NFR-2 at the request-context layer: the meeting-notes and sign-off APIs reject requests with no resolvable tenant context, an invalid/unknown tenant subdomain, or a tenant context that mismatches the JWT's tenant claim. Verify cross-tenant IDOR is blocked on BOTH read and write -- a user in Tenant B cannot sign, dispute, resolve, or export Tenant A's review by supplying Tenant A's reviewId/signoffId.

## 2. Related Requirements
- User Story: US-PRF-006
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-3 (sign-off), FR-7 (audit/IP)

## 3. Preconditions
- Tenants "acme" (with a known reviewId in Pending Employee Sign-Off) and "globex" exist.
- A valid user/JWT in globex; the TenantResolutionMiddleware resolves tenant from subdomain (dev: `X-Tenant-Subdomain`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme reviewId | known | target of IDOR |
| Bad subdomain | `nope.yourhrm.com` | unknown tenant |
| Mismatch | globex JWT + acme subdomain header | spoof |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call any sign-off endpoint with NO tenant context (no subdomain / no header) | Rejected before handler -- 400/404 (tenant cannot be resolved); no data returned, no write performed (NFR-2). |
| 2 | Call with an invalid/unknown subdomain (`nope`) | Tenant resolution fails -> rejected; not silently defaulted to any tenant. |
| 3 | Authenticate in globex but send `X-Tenant-Subdomain: acme` (mismatch) | Rejected -- the JWT tenant claim and resolved tenant must agree; no acme data exposed (NFR-2). |
| 4 | As a valid globex user, `POST .../reviews/{acme_reviewId}/signoff {decision:"Acknowledge"}` (IDOR write) | 404/403 -- global query filter scopes the lookup to globex; the acme review is invisible; NO signature appended to acme's review. |
| 5 | As a valid globex user, `POST .../reviews/{acme_reviewId}/signoff {decision:"Dispute", comments:"..."}` and `.../resolve-dispute` | 404/403 -- cannot dispute or resolve another tenant's review. |
| 6 | As a valid globex user, `GET .../reviews/{acme_reviewId}/export` | 404 -- cannot export another tenant's signed review (IDOR read). |

## 6. Postconditions
- No-context / invalid / mismatched-tenant requests are rejected; cross-tenant IDOR read and write on sign-off endpoints is blocked.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
