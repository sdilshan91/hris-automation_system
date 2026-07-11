---
id: TC-NTF-ISO-013
user_story: US-NTF-004
module: Notifications & Audit
priority: critical
type: security
status: fail
created: 2026-06-17
---

# TC-NTF-ISO-013: Tenant A admin sees ONLY Tenant A audit rows; Tenant B audit rows are invisible

## 1. Test Objective
Verify that an audit-log query by a Tenant A admin returns ONLY Tenant A audit records and never
Tenant B records. Confirms AC-5 / NFR-2 tenant isolation of the audit trail.

## 2. Related Requirements
- User Story: US-NTF-004
- Acceptance Criteria: AC-5 (Tenant A admin querying audit log sees only Tenant A records; Tenant B invisible)
- Non-Functional: NFR-2 (audit isolated by tenant; RLS deferred -> EF Core global query filter)

## 3. Preconditions
- Tenant A and Tenant B are both active with distinct audit rows produced by their own activity.
- `adminA` is authenticated in Tenant A; `adminB` in Tenant B.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A audit rows | >= 3 | produced by Tenant A activity |
| Tenant B audit rows | >= 3 | produced by Tenant B activity |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `adminA`, query the audit log (no filters) | Only Tenant A rows are returned -- every row's tenant_id = Tenant A |
| 2 | Confirm Tenant B rows are absent | NONE of Tenant B's audit rows appear in `adminA`'s result, regardless of pagination |
| 3 | As `adminB`, query the audit log | Only Tenant B rows are returned; Tenant A rows are absent |
| 4 | Confirm the filtering is automatic | The exclusion is enforced by the EF Core global query filter (tenant_id == current tenant), not by a caller-supplied filter |
| 5 | [CONDITIONAL / DEFERRED -- RLS] With PostgreSQL RLS provisioned | A raw query under Tenant A's context would also return only Tenant A rows -- documented as deferred defense-in-depth; the EF filter is the mechanism in force today |

## 6. Postconditions
- Each tenant admin sees only their own audit rows; no cross-tenant leakage.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
