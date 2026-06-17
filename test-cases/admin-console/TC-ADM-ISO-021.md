---
id: TC-ADM-ISO-021
user_story: US-ADM-008
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-021: Audit log list/detail tenant-scoped — Tenant A cannot see Tenant B's audit rows

## 1. Test Objective
Verify AC-1 / BR-3 / FR-1: audit records are scoped strictly to the tenant via the EF global query filter (`TenantId == ITenantContext.TenantId`). A Tenant A admin's list and detail queries return ONLY Tenant A rows; Tenant B audit rows are never visible. (Test Hints — Tenant isolation: create audit events in Tenant A and Tenant B; query as Tenant A admin; verify only Tenant A records returned.)

## 2. Related Requirements
- User Story: US-ADM-008
- Acceptance Criteria: AC-1 (scoped exclusively to current tenant; no other-tenant rows)
- Functional Requirements: FR-1 (filter by tenant_id = ITenantContext.TenantId)
- Business Rules: BR-3 (records scoped strictly to the tenant)

## 3. Preconditions
- Tenant Alpha has audit rows (writes by Alpha users); Tenant Beta has its own audit rows.
- Dana is `TenantAdmin` of Alpha; Bea is `TenantAdmin` of Beta.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Alpha rows | A-aud-1, A-aud-2 | |
| Beta rows | B-aud-1 | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana (Alpha), GET the audit list | Returns only A-aud-1, A-aud-2; B-aud-1 absent. |
| 2 | As Bea (Beta), GET the audit list | Returns only B-aud-1; no Alpha row present. |
| 3 | As Dana, apply filters (actor/action/keyword) | Filtered results never surface a Beta row. |
| 4 | Confirm counts | Each admin's total reflects only their own tenant's audit rows. |

## 6. Postconditions
- No cross-tenant audit row is ever listed; no state change.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
