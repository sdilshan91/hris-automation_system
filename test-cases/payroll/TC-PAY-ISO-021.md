---
id: TC-PAY-ISO-021
user_story: US-PAY-006
module: Payroll
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-PAY-ISO-021: Tenant B can never see or retrieve Tenant A's statutory rules, tax slabs, or social-security rules (cross-tenant read isolation)

## 1. Test Objective
Verify AC-4 / FR-8: the statutory configuration read surface (`statutory_rule`, `tax_slab`, `social_security_rule`) is tenant-scoped by EF Core global query filters + TenantInterceptor. A user authenticated in Tenant B can NEVER list, read, or resolve Tenant A's statutory rules -- not via the list/detail APIs and not by guessing a Tenant A statutory_rule_id / tax_slab_id. (US-PAY-006 AC-4/FR-8 say "RLS"; this platform enforces via EF query filters -- if Postgres RLS is later added on the statutory tables, extend Step 5 to assert session-level isolation as defense-in-depth.)

## 2. Related Requirements
- User Story: US-PAY-006
- Acceptance Criteria: AC-4
- Functional Requirements: FR-8
- Data Requirements: S7 (tenant_id on statutory_rule / tax_slab / social_security_rule)

## 3. Preconditions
- Tenant A "acme": statutory rules (IncomeTax slabs + EPF) for FY 2025-2026.
- Tenant B "globex": its own statutory rules; user authenticated in globex with `Payroll.*.All`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | owns rules |
| Tenant B | globex | attacker context |
| Target | acme statutory_rule_id `ruleA`, tax_slab_id `slabA` | A's data |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As a globex user (`X-Tenant-Subdomain: globex`), list statutory rules. | Only globex's rules returned; zero acme rules/slabs/social-security records. |
| 2 | As globex, GET the statutory-rule detail for acme's `ruleA`. | 404 Not Found (filtered out of globex scope); no acme data. |
| 3 | As globex, GET tax-slab `slabA` (acme) by id. | 404; no acme slab data. |
| 4 | As globex, resolve rules for a fiscal year/period. | Only globex's rule set is resolved; acme's slabs never appear in globex's calculation context. |
| 5 | Direct DB cross-check: query `tax_slab` for `slabA` with tenant context = globex. | EF global query filter returns zero rows (tenant_id=acme != globex). (If RLS is added, a globex DB session also returns zero.) |
| 6 | Confirm globex's own statutory rules remain fully accessible. | globex list/detail/resolve work normally -- isolation blocks only cross-tenant access. |

## 6. Postconditions
- Tenant B cannot read or enumerate Tenant A's statutory rules/slabs/social-security records by any path.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
