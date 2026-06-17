---
id: TC-NTF-ISO-020
user_story: US-NTF-005
module: Notifications & Audit
priority: high
type: security
status: draft
created: 2026-06-17
---

# TC-NTF-ISO-020: EF filter blocks cross-tenant reads across all viewer query paths; URL filter state cannot widen scope beyond tenant

## 1. Test Objective
Verify that EVERY query path exercised by the US-NTF-005 viewer -- list, multi-select filter, keyword
over JSONB, actor filter, detail, and export -- is constrained by the EF Core global tenant query
filter, and that URL-encoded filter state (FR-3), being shareable, can never be crafted to widen the
result scope beyond the requester's tenant. PostgreSQL RLS is deferred defense-in-depth.

## 2. Related Requirements
- User Story: US-NTF-005
- Acceptance Criteria: AC-5 (tenant isolation)
- Functional Requirements: FR-2 (filters), FR-3 (URL filter state), FR-4 (detail), FR-5 (export), FR-9 (meta-audit)
- Non-Functional: NFR-3 (isolation; RLS deferred -> EF filter)

## 3. Preconditions
- Tenants A and B both populated with audit rows; `adminA` authenticated in Tenant A.
- A shared/bookmarked filtered URL captured from Tenant A.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| shared URL | A's filtered view | replayed cross-tenant |
| injected params | tenant_id / action / actor / keyword | attempted scope-widening |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Exercise list, multi-select filter, JSONB keyword, actor filter, detail, export as adminA | All return ONLY Tenant A rows; the tenant predicate is applied automatically on each query path |
| 2 | Attempt to inject a `tenant_id` query param into the URL pointing at Tenant B | The injected tenant_id is ignored; results remain Tenant A-scoped (tenant comes from session/JWT, not the URL) |
| 3 | Take adminA's bookmarked filtered URL and open it as `adminB` (Tenant B) | adminB sees Tenant B's data for that filter shape (or empty) -- the URL carries filter intent, NOT a tenant scope; no Tenant A rows leak to adminB |
| 4 | Confirm writes/stamping | The meta-audit "AuditLog.View" produced by viewing is stamped with the current tenant via the interceptor |
| 5 | [CONDITIONAL / DEFERRED -- RLS] With RLS provisioned | Raw SQL without app.current_tenant_id returns zero rows -- deferred; EF filter is the control in force today |

## 6. Postconditions
- No viewer query path or URL filter state can read across tenant boundaries; writes are tenant-stamped.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
