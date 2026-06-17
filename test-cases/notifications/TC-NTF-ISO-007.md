---
id: TC-NTF-ISO-007
user_story: US-NTF-002
module: Notifications & Audit
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-NTF-ISO-007: EF global query filter blocks cross-tenant template reads; writes tenant-stamped (RLS deferred)

## 1. Test Objective
Verify the enforced data-layer isolation mechanism: EF Core global query filters scope every template
read to the current tenant, and the TenantInterceptor stamps tenant_id on new override rows on write.
Confirms the mechanism in force TODAY (Postgres RLS is a deferred extension).

## 2. Related Requirements
- User Story: US-NTF-002
- Acceptance Criteria: AC-5 (isolation), AC-3 (tenant_id from session on save)
- Functional Requirements: FR-10 (tenant_id from session)
- Non-Functional: NFR-2 (EF query filters; RLS deferred), NFR-6 (audit via interceptor)

## 3. Preconditions
- Tenant A and Tenant B each have at least one template row; both contexts available for testing.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A rows | leave_approved override | tenant_id=Tenant A |
| Tenant B rows | (defaults / own overrides) | tenant_id=Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With ITenantContext = Tenant A, query email templates via the app data layer | Only Tenant A overrides (+ applicable system defaults) returned; no Tenant B override rows |
| 2 | With ITenantContext = Tenant B, run the same query | Only Tenant B's rows (+ defaults); Tenant A's custom override is absent |
| 3 | As Tenant A, create a new override WITHOUT setting tenant_id explicitly | The TenantInterceptor stamps tenant_id = Tenant A on SaveChanges; the row is owned by Tenant A |
| 4 | Confirm the save produced an audit entry | NFR-6: a tenant-scoped audit row exists (SaveChanges interceptor) |
| 5 | (CONDITIONAL/DEFERRED — RLS) Run a raw SQL SELECT on the templates table without `app.current_tenant_id` set | If/when Postgres RLS is enabled this returns zero rows; documented as deferred — today isolation is enforced by the EF query filter in steps 1-2 |

## 6. Postconditions
- Reads tenant-filtered; writes tenant-stamped + audited; RLS path documented as deferred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
