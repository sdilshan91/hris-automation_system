---
id: TC-LV-ISO-027
user_story: US-LV-007
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-027: EF global query filters block cross-tenant access to holiday rows

## 1. Test Objective
Verify that the EF Core global query filter on the Holiday entity prevents cross-tenant reads at the data-access layer: with Tenant A's context resolved, queries against the holiday table return only Tenant A's rows, even for direct service/repository queries (NFR-2, Section 7). Tenant isolation here is EF global query filters + TenantInterceptor (RLS-equivalent per vault), not Postgres RLS.

## 2. Related Requirements
- User Story: US-LV-007
- Non-Functional Requirements: NFR-2
- Data Requirements (Section 7)
- Note: enforcement is the EF global query filter + TenantInterceptor (per docs/vault/modules/leave-management.md), satisfying the story's literal "RLS" requirement.

## 3. Preconditions
- Tenant "acme" and Tenant "globex" each have holiday rows in the shared `holiday` table.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme rows | N | tenant A |
| globex rows | M | tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With acme's context resolved, query `Holidays` (incl. the `IHolidayProvider` range read) | Only acme's rows are returned; globex's rows are filtered out by the global query filter. |
| 2 | Attempt to load a globex holiday by id under acme's context | No row is returned (filtered) -- a cross-tenant id is unreachable without `IgnoreQueryFilters()`. |
| 3 | Verify no service path calls `IgnoreQueryFilters()` for holidays | Holiday reads do not bypass the filter; only deliberate, non-holiday system paths (e.g. tenant lookup) ignore filters. |
| 4 | Confirm the soft-delete + tenant predicate compose | Queries return only non-deleted rows for the current tenant. |

## 6. Postconditions
- The holiday global query filter enforces tenant isolation at the data layer.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
