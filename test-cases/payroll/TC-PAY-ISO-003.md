---
id: TC-PAY-ISO-003
user_story: US-PAY-001
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-003: Cross-tenant payroll writes blocked; tenant_id is session-derived, not client-supplied (write isolation)

## 1. Test Objective
Verify AC-6 / FR-8: write operations on salary components, structures, and structure-component links derive `tenant_id` from the session/tenant context (TenantInterceptor), never from the request body. A user in Tenant A cannot create or modify records under Tenant B by injecting a foreign `tenant_id`, nor link a Tenant B component into a Tenant A structure.

## 2. Related Requirements
- User Story: US-PAY-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-1, FR-2, FR-3, FR-8

## 3. Preconditions
- Tenant "acme" and "globex" each have payroll data.
- An HR Officer with `Payroll.*.All` is authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | acme | Tenant A |
| Injected tenant_id | globex_tenant_id | body-injection attempt |
| Foreign component id | globex_component_id | cross-tenant link attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme, `POST /api/v1/payroll/components` with body containing `tenant_id` = globex_tenant_id | Created row is stamped with acme `tenant_id` (interceptor overrides the body); NOT globex. The injected value is ignored. |
| 2 | As acme, `PUT .../components/{globex_component_id}` (update a globex component by id) | 404 Not Found — the global query filter prevents acme from resolving/updating a globex row. |
| 3 | As acme, `POST .../structures/{acme_structure_id}/components` linking a `salary_component_id` = globex_component_id | Rejected (404/422) — cannot link a foreign-tenant component into an acme structure. No junction row created. |
| 4 | As acme, `DELETE .../structures/{globex_structure_id}` | 404 Not Found; the globex structure is untouched. |
| 5 | Verify the DB after all attempts | No globex row was created, modified, or deleted by the acme session; no acme row carries a globex `tenant_id`. |

## 6. Postconditions
- All payroll writes are confined to the authenticated tenant; no cross-tenant mutation via body injection or foreign ids.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
