---
id: TC-PAY-ISO-006
user_story: US-PAY-002
module: Payroll
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-PAY-ISO-006: Salary assignment APIs reject missing/invalid/mismatched tenant context

## 1. Test Objective
Verify FR-8: salary-assignment endpoints (assign, preview, override, bulk, revisions) require a valid tenant context. Requests with no resolvable tenant, an unknown subdomain, or a JWT tenant that mismatches the request subdomain are rejected; no salary data is read or written.

## 2. Related Requirements
- User Story: US-PAY-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8
- Data Requirements: S7

## 3. Preconditions
- Tenant "acme" Active with FT-IN structure and employee Ravi.
- Valid acme HR JWT available; a globex HR JWT also available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| No tenant header | (omitted) | request without X-Tenant-Subdomain / subdomain |
| Unknown subdomain | doesnotexist.yourhrm.com | unresolvable tenant |
| Mismatch | subdomain=acme, JWT tenant=globex | cross-tenant mismatch |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST /api/v1/payroll/employees/{id}/salary` with no tenant subdomain/header. | Rejected (400/401 per tenant-resolution middleware); no assignment created. |
| 2 | Call the same with subdomain `doesnotexist`. | Rejected; tenant not resolved; no data access. |
| 3 | Send subdomain=acme but a JWT whose `tenant_id`=globex. | Rejected (401/403 tenant mismatch); the request cannot operate across tenants. |
| 4 | Repeat for `salary/preview`, `salary/bulk`, and `salary/revisions`. | All reject without valid+matching tenant context; no breakdown, bulk write, or history returned. |
| 5 | Send a valid, matching acme context. | 200/201 success -- confirms rejection is context-based, not a broken endpoint. |

## 6. Postconditions
- Salary endpoints operate only under a valid, matching tenant context; otherwise rejected with no side effects.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
