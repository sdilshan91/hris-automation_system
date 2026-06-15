---
id: TC-PAY-ISO-008
user_story: US-PAY-002
module: Payroll
priority: high
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-008: Salary preview/breakdown caches are tenant-scoped (no cross-tenant cache leak)

## 1. Test Objective
Verify FR-8 / NFR-2 cache isolation: any caching of CTC breakdown / preview computations or employee-salary reads is keyed by tenant (`tenant:{tenantId}:payroll:salary:...`) so that one tenant's cached salary data can never be served to another tenant. (Cache layer per S10 assumed available; if salary preview is computed on-demand without caching today, this TC is CONDITIONAL and asserts that no shared/global cache key is used.)

## 2. Related Requirements
- User Story: US-PAY-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8
- Non-Functional: NFR-2
- Data Requirements: S7

## 3. Preconditions
- Tenant "acme" employee Ravi and Tenant "globex" employee Lena both have assignments.
- Both tenants use the same structure code FT-IN (different rows per tenant) to maximize collision risk.
- HR users authenticated in each tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Cache key pattern | tenant:{tenantId}:payroll:salary:* | tenant-scoped |
| Tenant A CTC | 600000 | acme/Ravi |
| Tenant B CTC | 900000 | globex/Lena |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, preview/fetch Ravi's breakdown (CTC 600,000) to warm any cache. | Breakdown returned; if cached, key includes acme tenant_id. |
| 2 | As globex HR, preview/fetch Lena's breakdown (CTC 900,000). | Returns globex values (900,000-derived); never acme's cached 600,000 breakdown. |
| 3 | Inspect cache keys (if a cache is in use). | All salary cache keys are prefixed with the tenant id; no global/un-prefixed key holds salary data. |
| 4 | Update Ravi's assignment (acme) and re-fetch. | acme cache invalidated/refreshed; globex cache entries untouched. |
| 5 | Rapidly alternate acme/globex preview requests. | Each response reflects only the requesting tenant's data; no cross-tenant bleed under concurrency. |

## 6. Postconditions
- Salary preview/read caches are strictly tenant-scoped; no cross-tenant cache leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
