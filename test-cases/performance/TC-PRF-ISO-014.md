---
id: TC-PRF-ISO-014
user_story: US-PRF-004
module: Performance Management
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-014: Cycle APIs reject missing/invalid/mismatched tenant context + cross-tenant IDOR block (NFR-2)

## 1. Test Objective
Verify NFR-2: every cycle endpoint requires a valid resolved tenant context and a valid bearer token; requests with no tenant context, an unknown/invalid subdomain, or a tenant context that mismatches the JWT's tenant are rejected. A valid user in Tenant B cannot reach Tenant A's cycle by manipulating the ID (IDOR).

## 2. Related Requirements
- User Story: US-PRF-004
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1, FR-7

## 3. Preconditions
- Tenant "acme" has cycle "FY26 Annual Review" (id known).
- Valid HR users exist in both acme and globex.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme cycle id | {acme_cycle_id} | target for IDOR |
| globex user | valid bearer in globex | |
| Bad subdomain | does-not-exist | unknown tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET .../cycles` with NO tenant context (no subdomain / no `X-Tenant-Subdomain`) | Rejected — TenantResolutionMiddleware yields no tenant; 400/401; no data returned. |
| 2 | Call with an unknown subdomain `does-not-exist` | Rejected; no tenant resolved; no cross-tenant fallthrough. |
| 3 | Authenticate as a globex user but send `X-Tenant-Subdomain: acme` (JWT tenant != resolved tenant) | Rejected (401/403) — mismatched tenant context is not honored; never serves acme data under globex auth. |
| 4 | As a valid globex user with globex context, `GET .../cycles/{acme_cycle_id}` and `PUT/POST` on it (IDOR) | 404/403 — the acme cycle id is not resolvable under globex; no read or write occurs. |
| 5 | With no bearer token at all, call any cycle endpoint | 401 Unauthorized. |

## 6. Postconditions
- All cycle endpoints fail closed without a valid, matching tenant context; cross-tenant IDOR is blocked.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
