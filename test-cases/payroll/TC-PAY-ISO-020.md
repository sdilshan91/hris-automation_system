---
id: TC-PAY-ISO-020
user_story: US-PAY-005
module: Payroll
priority: high
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-020: Employee payslip list/detail caching is tenant- and employee-scoped (no cross-tenant or cross-employee cache leak)

## 1. Test Objective
Verify FR-8 / NFR-1 / NFR-4: any cache used to satisfy the My Payslips list / detail (or any signed-URL / download keying) is keyed by both `tenant_id` AND `employee_id`, so a cached payslip response for one employee/tenant can never be served to another. Asserts cache-key composition and invalidation. (CONDITIONAL: if no cache layer exists for the self-payslip read today and responses are computed on demand, this TC asserts that no shared/global cache key is used and the query is always tenant+employee filtered.)

## 2. Related Requirements
- User Story: US-PAY-005
- Acceptance Criteria: AC-4
- Functional Requirements: FR-8
- Non-Functional Requirements: NFR-1, NFR-4
- Reference: tenant-scoped cache-key convention `tenant:{tenantId}:...` (cf. TC-PAY-ISO-004/008/016)

## 3. Preconditions
- Tenant A "acme": EMP-A01 with Finalized payslips.
- Tenant B "globex": EMP-B01 with Finalized payslips.
- Second acme employee EMP-A02 (to test cross-employee within the same tenant).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Expected key shape | tenant:{tenantId}:employee:{employeeId}:payslips:* | tenant+employee scoped |
| Tenants | acme, globex | cross-tenant |
| Employees | EMP-A01, EMP-A02 (acme), EMP-B01 (globex) | cross-employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As EMP-A01, load My Payslips (populates cache if present). | Response cached (if caching enabled) under a key containing both acme tenant id and EMP-A01's employee id. |
| 2 | As EMP-A02 (same tenant acme), load My Payslips. | EMP-A02 gets only their own slips; the EMP-A01 cache entry is NOT served (employee-scoped key); no EMP-A01 data leaks. |
| 3 | As EMP-B01 (globex), load My Payslips. | globex gets only its slips; the acme cache entry is never reused (tenant-scoped key). |
| 4 | Inspect cache keys (or confirm on-demand if no cache). | Keys are namespaced by tenant + employee; no shared/global key. If no cache: each request issues a tenant+employee-filtered query (CONDITIONAL note applies). |
| 5 | Finalize a new run for EMP-A01 (new slip appears). | EMP-A01's cached list is invalidated / the new slip appears on next load; no stale or cross-employee entry served. |
| 6 | Verify download/preview keying (if signed URLs used). | Any download token/URL is bound to tenant+employee+payslip; not reusable across employees or tenants. |

## 6. Postconditions
- All payslip caches/tokens are tenant- and employee-scoped; no cross-tenant or cross-employee cache leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
