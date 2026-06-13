---
id: TC-LV-ISO-017
user_story: US-LV-005
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-017: Manager in Tenant A cannot approve or reject Tenant B's leave request

## 1. Test Objective
Verify full tenant isolation of the approve/reject actions: a manager authenticated in Tenant A cannot approve or reject a leave request that belongs to Tenant B, even with a known Tenant B request UUID. (Test Hint: Manager in Tenant A cannot approve requests in Tenant B.)

## 2. Related Requirements
- User Story: US-LV-005
- Non-Functional Requirements: NFR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with manager "Robert Lee" (`Leave.Approve.Team`) and a pending request `R_acme`.
- Tenant "globex" exists with manager "Sara Kim" (`Leave.Approve.Team`) and a pending request `R_globex`.
- Robert is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Robert Lee, R_acme Pending |
| Tenant B | globex | Sara Kim, R_globex Pending |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert (acme), `POST /api/v1/leaves/{R_globex}/approve` using the known globex request UUID | 404 Not Found (filtered by the EF global query filter -- the row is invisible in acme's context); R_globex stays Pending. |
| 2 | As Robert (acme), `POST /api/v1/leaves/{R_globex}/reject` with a reason | 404 Not Found; no state change to R_globex; no globex ledger/history/audit entry written. |
| 3 | Switch to globex (Sara) and approve R_globex | Succeeds within globex's own context (positive control). |
| 4 | Verify no cross-tenant side effects | No acme ledger/history rows reference globex, and vice versa; balances and history remain tenant-local. |

## 6. Postconditions
- Cross-tenant approve/reject attempts return 404 with no side effects in either direction.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
