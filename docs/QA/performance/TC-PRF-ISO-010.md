---
id: TC-PRF-ISO-010
user_story: US-PRF-003
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-010: Manager-review APIs reject missing/invalid/mismatched tenant context + block cross-tenant IDOR

## 1. Test Objective
Verify NFR-2 on the request surface: the manager-review endpoints (list, get-by-id, submit, reopen) reject requests with no tenant context, an invalid/unknown tenant subdomain, or a JWT whose tenant claim mismatches the resolved subdomain. A user authenticated in Tenant A cannot act on a Tenant B review by supplying B's review id (IDOR), even with a valid A session.

## 2. Related Requirements
- User Story: US-PRF-003
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1

## 3. Preconditions
- acme and globex active. acme user Ravi with a valid acme JWT. A known globex review id R_globex.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| No tenant context | (omit X-Tenant-Subdomain / subdomain) | reject |
| Invalid subdomain | `nope.yourhrm.com` | reject |
| Mismatched | acme JWT + `X-Tenant-Subdomain: globex` | reject |
| IDOR target | R_globex | acme session must not reach it |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call a review endpoint with NO tenant context | Rejected (400/401) -- tenant resolution fails before data access. |
| 2 | Call with an unknown/invalid subdomain | Rejected (404/400) -- no tenant resolved; no data returned. |
| 3 | Send Ravi's acme JWT with `X-Tenant-Subdomain: globex` | Rejected (401/403) -- tenant claim vs resolved tenant mismatch; no globex data. |
| 4 | As valid acme Ravi, GET/POST/reopen review id R_globex | 404/403 -- cross-tenant IDOR blocked; nothing returned or mutated. |
| 5 | As valid acme Ravi with acme subdomain, act on an acme review | Allowed (control). |

## 6. Postconditions
- Missing/invalid/mismatched tenant context is rejected; cross-tenant IDOR on review ids is blocked.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
