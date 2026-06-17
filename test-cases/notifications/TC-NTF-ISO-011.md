---
id: TC-NTF-ISO-011
user_story: US-NTF-003
module: Notifications & Audit
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-NTF-ISO-011: EF global filter blocks cross-tenant preference reads; writes tenant-stamped (RLS deferred)

## 1. Test Objective
Verify the read/write isolation mechanism in force today: the EF Core global query filter excludes
other tenants' preference rows from every query, and the TenantInterceptor stamps tenant_id on new
preference records from the resolved tenant context. The Postgres RLS expectation is documented as
deferred/CONDITIONAL.

## 2. Related Requirements
- User Story: US-NTF-003
- Acceptance Criteria: AC-5 (per-tenant-membership isolation)
- Functional Requirements: FR-8 (tenant_id + user_id set on all preference records)
- Non-Functional: NFR-2 (tenant isolation via EF Core global query filters; Postgres RLS deferred)
- Business Rules: BR-4 (per tenant membership)

## 3. Preconditions
- Tenant A and Tenant B each have preference rows for their users.
- Test harness can execute queries under a resolved Tenant A context and (conditionally) raw SQL.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A rows | present | |
| Tenant B rows | present | must never appear in Tenant A queries |
| RLS session var | app.current_tenant_id | CONDITIONAL -- RLS deferred |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Under Tenant A context, query all preference records via the app/EF | Only Tenant A rows are returned; Tenant B rows are excluded by the global query filter |
| 2 | Create a new preference record under Tenant A context | Record is auto-stamped tenant_id=Tenant A by the TenantInterceptor; user_id bound to the session user (FR-8) |
| 3 | Attempt to create a preference with a manually set tenant_id=Tenant B under a Tenant A session | The interceptor enforces Tenant A; the row is NOT persisted under Tenant B |
| 4 | (CONDITIONAL -- RLS deferred) Run raw SQL against the preferences table WITHOUT setting app.current_tenant_id | DOCUMENTED EXPECTATION when RLS is enabled: zero rows. Today RLS is not provisioned, so this step is recorded as deferred, not a coverage gap |
| 5 | Confirm IgnoreQueryFilters is not used on preference reads in app code | Application preference reads always go through the tenant filter |

## 6. Postconditions
- Reads are tenant-filtered and writes are tenant-stamped; RLS hardening tracked as deferred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
