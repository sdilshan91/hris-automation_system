---
id: TC-PRF-ISO-026
user_story: US-PRF-007
module: Performance Management
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-026: Dashboard APIs reject missing/invalid/mismatched tenant context + cross-tenant IDOR (overview/trend/drill-down/export)

## 1. Test Objective
Verify NFR-2: every dashboard endpoint (overview, trend, top/bottom performers, department drill-down, export) requires a valid, consistent tenant context and rejects requests with missing, invalid, or mismatched tenant context. A user authenticated in one tenant cannot retrieve another tenant's analytics by supplying that tenant's cycle/department/employee id (IDOR), and a token whose tenant claim disagrees with the resolved subdomain is rejected.

## 2. Related Requirements
- User Story: US-PRF-007
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-4 (filters), FR-5 (drill-down), FR-7 (trend), FR-8 (export)

## 3. Preconditions
- Tenant "acme" (with FY26 cycle + Engineering department + employees) and Tenant "globex" both exist with their own HR Officers.
- Known acme ids: cycleId, departmentId, employeeId.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | target of IDOR |
| Tenant B | globex | attacker context |
| acme ids | FY26 cycleId, eng deptId, employeeId | direct-id probes |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET .../performance/dashboard/overview` with NO tenant context (no subdomain / no `X-Tenant-Subdomain`) | Rejected -- no tenant resolved; 400/401; never a default/global aggregate. |
| 2 | Call with an INVALID/unknown tenant subdomain | Rejected; tenant resolution fails (404/401); no data. |
| 3 | Authenticated as globex HR, set `X-Tenant-Subdomain: acme` (mismatch vs the JWT tenant claim) | Rejected -- tenant-context mismatch; the request does not return acme data (NFR-2). |
| 4 | As globex HR (globex context), `GET .../dashboard/overview?cycleId={acme_FY26}` and `.../departments/{acme_engId}/employees` and `.../export?cycleId={acme_FY26}` | Empty / 404 / 403 -- IDOR by acme ids blocked by the tenant query filter; never acme aggregates or rosters. |
| 5 | As globex HR, request acme's trend `.../dashboard/trend?cycleIds={acme cycle ids}` | acme cycle ids resolve to nothing in globex context; no acme series returned. |
| 6 | As globex HR, attempt to drill into an acme employee id | 404/403; no acme individual score exposed. |

## 6. Postconditions
- Dashboard endpoints reject missing/invalid/mismatched tenant context and block cross-tenant IDOR across overview, trend, drill-down, and export.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
