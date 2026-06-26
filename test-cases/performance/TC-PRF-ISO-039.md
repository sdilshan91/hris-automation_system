---
id: TC-PRF-ISO-039
user_story: US-PRF-010
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-039: Cross-tenant WRITE block -- server-derived tenant_id on recommendations/budgets (no body injection) + foreign employee_id/cycle_id/approver/budget_id rejected (NFR-2)

## 1. Test Objective
Verify NFR-2 on writes: when creating/updating a recommendation or budget, the `tenant_id` is always server-derived from the resolved tenant context (via TenantInterceptor) -- a `tenant_id` injected in the request body is IGNORED and never honored. Foreign-key references that belong to another tenant (employee_id, cycle_id, approver/user id, budget_id) are REJECTED, so a caller cannot create a recommendation against another tenant's employee/cycle or attach another tenant's budget/approver.

## 2. Related Requirements
- User Story: US-PRF-010
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1 (recommendation create), FR-4 (approver chain), FR-8 (budget)
- Data Requirements: S7 (tenant_id stamped server-side; FK integrity within tenant)

## 3. Preconditions
- Tenant "acme" HR Officer Pat Ng (`Performance.Publish.All`) authenticated; Tenant "globex" has an employee `globex_empId`, a cycle `globex_cycleId`, a budget `globex_budgetId`, and a user `globex_userId`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Injected body tenant_id | globex_id | must be ignored |
| Foreign employee_id | globex_empId | reject |
| Foreign cycle_id | globex_cycleId | reject |
| Foreign budget_id | globex_budgetId | reject |
| Foreign approver | globex_userId | reject |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Pat (acme), `POST .../recommendations` with `tenant_id = globex_id` injected in the body | The injected tenant_id is IGNORED; the recommendation is stamped with acme's tenant_id (server-derived via TenantInterceptor). It is an acme row, never a globex row. |
| 2 | As Pat, create a recommendation referencing `globex_empId` (a foreign employee) | Rejected -- the employee resolves only within acme; a foreign employee_id is not found / 400. No recommendation persists. |
| 3 | As Pat, create/auto-generate against `globex_cycleId` | Rejected -- the cycle resolves only within acme; no recommendation/suggestion created against a foreign cycle. |
| 4 | As Pat, attach `globex_budgetId` to an acme recommendation | Rejected -- the budget FK must be an acme budget; a foreign budget_id is rejected. |
| 5 | As Pat, configure an approver chain referencing `globex_userId` | Rejected -- approvers must be acme users; a foreign approver is rejected. |
| 6 | Verify persisted rows | Every recommendation/budget row created carries acme's tenant_id; no foreign-tenant FK was accepted. |

## 6. Postconditions
- tenant_id is always server-derived (body injection ineffective); foreign-tenant employee/cycle/budget/approver references are rejected. No cross-tenant write or FK linkage created. Tenant-scoped to acme.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
