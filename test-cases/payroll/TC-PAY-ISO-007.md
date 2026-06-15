---
id: TC-PAY-ISO-007
user_story: US-PAY-002
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-007: Cross-tenant salary writes blocked; tenant_id is session-derived, not client-supplied (incl. bulk)

## 1. Test Objective
Verify FR-8 / NFR write-isolation: salary assignment writes (single + bulk) cannot target another tenant's employee, and `tenant_id` on `employee_salary_component` / `salary_revision_history` is always stamped from the resolved tenant context (TenantInterceptor), never trusted from the request body. Attempts to assign a salary to a Tenant A employee while authenticated in Tenant B, or to inject a foreign `tenant_id`, fail without creating cross-tenant rows.

## 2. Related Requirements
- User Story: US-PAY-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1, FR-5, FR-8
- Data Requirements: S7 (tenant_id auto-stamped)

## 3. Preconditions
- Tenant "acme" has employee Ravi; Tenant "globex" has employee Lena.
- HR with `Payroll.*.All` authenticated in globex (Tenant B).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | globex | Tenant B |
| Target employee | Ravi (acme) | Tenant A employee |
| Injected tenant_id | acme_id in request body | should be ignored/rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As globex HR, `POST /api/v1/payroll/employees/{ravi_id}/salary` (Ravi belongs to acme). | Rejected (404/403); Ravi is invisible/inaccessible from globex; no row created in either tenant. |
| 2 | As globex HR, assign Lena (own tenant) but inject `tenant_id`=acme_id in the request body. | The injected tenant_id is ignored; the persisted row is stamped globex via TenantInterceptor. |
| 3 | Bulk assign as globex HR including Ravi's (acme) employee id in the batch. | Ravi's row is rejected/skipped (not found in globex); only globex employees assigned; no acme rows written. |
| 4 | Verify persisted rows. | All new `employee_salary_component` and `salary_revision_history` rows carry `tenant_id`=globex; zero rows attributed to acme. |
| 5 | Switch to acme; confirm Ravi has no spurious globex-originated assignment. | Ravi's salary data unaffected; no cross-tenant write leaked through. |

## 6. Postconditions
- Salary writes are confined to the authenticated tenant; tenant_id is server-derived and cannot be spoofed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
