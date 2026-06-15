---
id: TC-PAY-ISO-023
user_story: US-PAY-006
module: Payroll
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-023: Cross-tenant statutory writes blocked; tenant_id is session-derived (body-injected tenant_id ignored; foreign statutory_rule_id link rejected)

## 1. Test Objective
Verify AC-4 / FR-8: writes to statutory tables are stamped with the resolved tenant's id by the TenantInterceptor, never from the request body. A globex-authenticated user who (a) injects `tenant_id = acme` in a create/update body, or (b) creates a tax_slab / social_security_rule whose `statutory_rule_id` points at an acme `statutory_rule`, cannot write into or attach to Tenant A's data. The injected tenant_id is ignored (record lands in globex) and the cross-tenant foreign link is rejected (404/400).

## 2. Related Requirements
- User Story: US-PAY-006
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1, FR-2, FR-8
- Data Requirements: S7 (tax_slab.statutory_rule_id, social_security_rule.statutory_rule_id FKs)

## 3. Preconditions
- Tenant A "acme" with statutory_rule `ruleA`.
- Tenant B "globex" with user `globexUser` (`Payroll.*.All`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Injected body tenant_id | acmeTenantId | must be ignored |
| Foreign parent link | acme `ruleA` (statutory_rule_id) | reject in globex |
| Foreign component link | acme applicable_component_ids[] | reject in globex |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As globexUser, POST a new statutory_rule with `tenant_id = acmeTenantId` injected in the body. | The injected tenant_id is ignored; the record is created under globex (session-derived); acme is unaffected. |
| 2 | As globexUser, POST a tax_slab whose `statutory_rule_id` = acme `ruleA`. | Rejected (404/400) -- the parent rule is not visible/owned in globex; no slab attached to acme's rule. |
| 3 | As globexUser, POST a social_security_rule with `applicable_component_ids` referencing acme salary components. | Rejected -- foreign component links outside globex are not resolvable; no cross-tenant attachment. |
| 4 | As globexUser, PUT/PATCH acme's `ruleA` directly. | 404 Not Found (acme rule invisible to globex); no acme mutation. |
| 5 | As globexUser, DELETE acme's `ruleA` / `slabA`. | 404; acme records remain intact. |
| 6 | Verify acme's data after all attempts. | acme statutory_rule/tax_slab/social_security_rule counts and values unchanged; globex's legitimate writes landed only in globex. |

## 6. Postconditions
- Cross-tenant statutory writes/links are impossible; tenant_id is always session-derived; acme data untouched.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
