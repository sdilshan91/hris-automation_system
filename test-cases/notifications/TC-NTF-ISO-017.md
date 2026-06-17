---
id: TC-NTF-ISO-017
user_story: US-NTF-005
module: Notifications & Audit
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-NTF-ISO-017: Tenant A admin sees ONLY Tenant A audit rows in the viewer; Tenant B rows invisible (RLS deferred -> EF filter)

## 1. Test Objective
Verify AC-5 / NFR-3 for the viewer: a Tenant A admin browsing/filtering the audit log sees ONLY Tenant
A rows -- including the new "AuditLog.View" meta-audit rows -- and never Tenant B rows, across all
filter combinations and pagination. Tenant isolation is enforced by the EF Core global query filter
today; PostgreSQL RLS is deferred defense-in-depth.

## 2. Related Requirements
- User Story: US-NTF-005
- Acceptance Criteria: AC-5 (Tenant A admin sees only Tenant A records; Tenant B invisible)
- Non-Functional: NFR-3 (isolation via tenant filter; RLS deferred)
- Business Rules: BR-5 (meta-audit rows are themselves tenant-scoped)

## 3. Preconditions
- Tenants A and B both active with distinct audit rows (incl. distinct actors, actions, JSONB content).
- `adminA` authenticated in Tenant A; `adminB` in Tenant B.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A rows | >= 5 | varied actions/actors/JSONB |
| Tenant B rows | >= 5 | including content that would match A's keyword |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `adminA`, view the audit log unfiltered | Every returned row's tenant_id = Tenant A; no Tenant B rows on any page |
| 2 | As `adminA`, apply a keyword that ALSO matches Tenant B JSONB content | Only Tenant A rows containing the keyword are returned; Tenant B matches are NOT leaked via keyword search |
| 3 | As `adminA`, filter by an actor name that exists in BOTH tenants | Only Tenant A's actor rows returned (actor autocomplete + filter tenant-scoped, see TC-NTF-ISO-018) |
| 4 | Confirm meta-audit isolation | The "AuditLog.View" row from adminA is Tenant A-scoped; adminB cannot see it (BR-5) |
| 5 | As `adminB`, view the audit log | Only Tenant B rows; Tenant A rows absent |
| 6 | Confirm enforcement mechanism | Exclusion is enforced by the EF Core global query filter (tenant_id == current tenant), not a caller-supplied filter |
| 7 | [CONDITIONAL / DEFERRED -- RLS] With PostgreSQL RLS provisioned | A raw query under Tenant A's context would also return only Tenant A rows -- deferred defense-in-depth; EF filter is the control in force today |

## 6. Postconditions
- Each admin sees only their tenant's audit rows across all filters/pages; no cross-tenant leakage.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
