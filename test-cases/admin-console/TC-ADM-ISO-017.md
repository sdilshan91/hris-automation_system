---
id: TC-ADM-ISO-017
user_story: US-ADM-007
module: Admin Console
priority: critical
type: security
status: pass
exec_note: "2026-07-03 API-layer isolation probe (acme tenantadmin JWT): cross-tenant arm (X-Tenant-Subdomain: techoneglobal) => 403 cross_tenant_denied; same-tenant arm (acme) => 200. TenantAccessGuardMiddleware enforced. No leak."
created: 2026-06-17
---

# TC-ADM-ISO-017: BR-7 — workflow list/read is tenant-scoped; Tenant A cannot see Tenant B's workflows

## 1. Test Objective
Verify BR-7 / FR-7: workflow definitions are entirely tenant-scoped — Tenant A's workflows are invisible to Tenant B. The list and read endpoints return ONLY the current tenant's definitions (EF global query filter on `TenantId`); a Tenant B admin never sees Tenant A's workflows. (Test Hint "Cross-tenant isolation": as Tenant A admin, attempt to read Tenant B's workflow definitions; verify 404 or empty result.)

## 2. Related Requirements
- User Story: US-ADM-007
- Acceptance Criteria: AC-1 (only current tenant's workflows shown)
- Functional Requirements: FR-7 (scoped via ITenantContext)
- Business Rules: BR-7 (A invisible to B)

## 3. Preconditions
- Tenant Alpha has workflows WF-Alpha-1 (Leave), WF-Alpha-2 (Expense).
- Tenant Beta has workflows WF-Beta-1 (Leave).
- Dana is `TenantAdmin` of Alpha; Bea is `TenantAdmin` of Beta.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Alpha workflows | WF-Alpha-1, WF-Alpha-2 | |
| Beta workflows | WF-Beta-1 | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana (Alpha), GET workflow list | Returns only WF-Alpha-1, WF-Alpha-2; WF-Beta-1 absent. |
| 2 | As Bea (Beta), GET workflow list | Returns only WF-Beta-1; no Alpha workflow present. |
| 3 | As Dana, GET WF-Beta-1 by its id | 404 (existence not disclosed — query filter scopes the row out). |
| 4 | Confirm counts | Each admin's list count reflects only their own tenant's definitions. |

## 6. Postconditions
- No cross-tenant workflow is ever listed or readable; no state change.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
