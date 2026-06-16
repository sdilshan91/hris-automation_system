---
id: TC-PRF-ISO-009
user_story: US-PRF-003
module: Performance Management
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-009: Manager reviews in Tenant A are invisible from Tenant B (cross-tenant read), including by direct review ID; HR reopen is tenant-scoped

## 1. Test Objective
Verify NFR-2 cross-tenant read isolation on the new `review` (manager-assessment) table: a manager or HR in Tenant B can never see Tenant A's manager reviews -- not via the Team Reviews dashboard, list endpoints, nor by requesting a known Tenant A review id directly. An HR Officer with `Performance.Review.All` is bounded to their OWN tenant -- `.All` means all employees within the tenant, never across tenants -- and cannot reopen a review in another tenant.

## 2. Related Requirements
- User Story: US-PRF-003
- Non-Functional Requirements: NFR-2
- Business Rules: BR-3 (HR .All is tenant-bounded)

## 3. Preconditions
- Two active tenants: acme (subdomain `acme`) and globex (subdomain `globex`).
- acme has a submitted manager review R_acme for Asha in FY26-H1; its id is known.
- globex has its own manager (Ben) and HR Officer (with `Performance.Review.All`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme review id | R_acme | known to the tester |
| globex manager | Ben | Performance.Review.Team |
| globex HR | Performance.Review.All | tenant-bounded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As globex Ben, open Team Reviews / list manager reviews | Zero acme reviews appear; only globex data is returned (EF global query filter scopes by `tenant_id == globex`). |
| 2 | As globex Ben (or globex HR), GET review by direct id R_acme | 404/403 -- the acme review is not resolvable from the globex tenant context (no cross-tenant read by id). |
| 3 | As globex HR (`Performance.Review.All`), attempt to reopen R_acme | 404/403 -- `.All` is bounded to globex; it cannot reopen an acme review (BR-3). |
| 4 | As acme Ravi, confirm R_acme is still readable | Control: acme context resolves R_acme normally. |

## 6. Postconditions
- No manager review crosses a tenant boundary on read or reopen, including by direct id; HR `.All` is tenant-bounded.

> Note: this platform enforces tenant isolation via EF Core global query filters + TenantInterceptor, not PostgreSQL RLS. US-PRF-003 NFR-2 / S7 name RLS on the Review table; that is documented as an extension point -- the EF mechanism is what is asserted here.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
