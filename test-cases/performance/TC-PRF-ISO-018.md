---
id: TC-PRF-ISO-018
user_story: US-PRF-005
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-018: 360 APIs reject missing/invalid/mismatched tenant context + cross-tenant IDOR (NFR-2)

## 1. Test Objective
Verify NFR-2: every 360 endpoint requires a valid, resolved tenant context and rejects requests with no tenant, an invalid/unknown subdomain, or a tenant that mismatches the caller's JWT. A caller authenticated in Tenant A cannot operate on Tenant B's 360 resources by supplying B's IDs (IDOR).

## 2. Related Requirements
- User Story: US-PRF-005
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1, FR-8

## 3. Preconditions
- Tenants "acme" and "globex" both have 360 reviews with known IDs.
- HR Officer authenticated in acme; globex resource IDs are known to the tester.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Caller tenant | acme | from JWT |
| Target IDs | globex reviewee/assignment/results IDs | for IDOR attempts |
| Bad contexts | none / invalid subdomain / mismatched | rejection cases |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call a 360 endpoint with NO tenant context (no subdomain / no `X-Tenant-Subdomain`) | Rejected — request cannot resolve a tenant; 400/401, no data returned. |
| 2 | Call with an invalid/unknown subdomain | Tenant resolution fails; rejected; no data. |
| 3 | Authenticate in acme but send `X-Tenant-Subdomain: globex` (mismatch vs JWT tenant) | Rejected — tenant context must match the JWT's tenant; no globex data served. |
| 4 | As acme, `GET .../performance/360/{globex_revieweeId}/results` and `POST .../feedback/{globex_assignmentId}/submit` | 404/403 — query filters + tenant check block cross-tenant access by ID (IDOR), for both read and write. |
| 5 | As acme, target a valid acme resource | 200 — legitimate same-tenant access still works. |

## 6. Postconditions
- 360 endpoints require a valid, matching tenant context; cross-tenant IDOR (read + write) is blocked.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
