---
id: TC-LV-ISO-008
user_story: US-LV-002
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-008: Cache keys for leave balances are tenant-scoped (DEFERRED -- partial)

## 1. Test Objective
Verify that when Redis caching is implemented for leave balances, cache keys follow the pattern `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}` and are fully tenant-scoped. A cache lookup for Tenant A must never return data cached for Tenant B, even if the employee_id and leave_type_id happen to match across tenants.

**DEFERRED (partial):** Redis caching for leave balances is not yet implemented. This test case documents the required cache isolation pattern. When caching is implemented, activate all steps. The Hangfire job tenant-scoping (Step 7-8) is testable now.

## 2. Related Requirements
- User Story: US-LV-002
- Non-Functional Requirements: NFR-2, NFR-3
- Functional Requirements: FR-6

## 3. Preconditions
- **DEFERRED (partial)** -- Redis caching must be implemented for leave balances.
- Tenant "acme" exists with employees and leave balances.
- Tenant "globex" exists with employees and leave balances.
- Redis server accessible for key inspection.

## 4. Test Data
| Item | Tenant A (acme) | Tenant B (globex) |
|------|----------------|-------------------|
| Tenant ID | acme_tenant_id | globex_tenant_id |
| Cache Key Example | tenant:acme_id:leave_balance:emp1:lt1 | tenant:globex_id:leave_balance:emp1:lt1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **DEFERRED** -- As acme user, query leave balance for Employee A, Annual Leave | Cache key `tenant:{acme_id}:leave_balance:{empA_id}:{annual_id}` created in Redis. |
| 2 | **DEFERRED** -- As globex user, query leave balance for Employee B, Annual Leave | Cache key `tenant:{globex_id}:leave_balance:{empB_id}:{annual_id}` created in Redis (different tenant prefix). |
| 3 | **DEFERRED** -- Inspect Redis keys | Both keys exist with different tenant prefixes. No overlap. |
| 4 | **DEFERRED** -- Invalidate acme's cache by updating an entitlement rule | Only `tenant:{acme_id}:*` balance keys are invalidated. Globex keys remain. |
| 5 | **DEFERRED** -- Verify globex balance cache is NOT invalidated by acme's rule change | Globex cache entries still present with original TTL. |
| 6 | **DEFERRED** -- Attempt cache key collision: if hypothetically two tenants had the same employee UUID (impossible in practice but test the key structure) | Keys are different because tenant_id prefix differs. |
| 7 | Verify Hangfire accrual job processes only employees within the tenant it was triggered for | When triggered for acme, only acme employees are processed. Globex employees are untouched. |
| 8 | Verify Hangfire job does not write ledger entries with mismatched tenant_id | All ledger entries created by the job have `tenant_id` matching the job's tenant context. |

## 6. Postconditions
- **DEFERRED (partial)** -- Cache keys are tenant-scoped with no cross-tenant leakage.
- Hangfire jobs (testable now) process only tenant-scoped data.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
