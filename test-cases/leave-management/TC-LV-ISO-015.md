---
id: TC-LV-ISO-015
user_story: US-LV-004
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-015: EF global query filters block cross-tenant access to pending leave_request rows

## 1. Test Objective
Verify that tenant isolation for the pending queue is enforced at the data-access layer by EF Core global query filters (the codebase's isolation mechanism; the story's "RLS" is realized via EF filters + TenantInterceptor, per the vault), so that even a direct query under Tenant A's context cannot read Tenant B's `leave_request` rows.

## 2. Related Requirements
- User Story: US-LV-004
- Non-Functional Requirements: NFR-3
- Data Requirements: Section 7 (pending query and ix_leave_pending)

## 3. Preconditions
- Tenants "acme" and "globex" each have pending `leave_request` rows.
- The `AppDbContext` applies a global query filter `lr => lr.TenantId == _tenantContext.TenantId` (when resolved) to `leave_request`.
- The `TenantInterceptor` stamps `TenantId` on writes.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme pending rows | 3 | Tenant A |
| globex pending rows | 4 | Tenant B |
| Isolation mechanism | EF Core global query filter + TenantInterceptor | Not Postgres RLS |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set the tenant context to acme and query pending `leave_request` via the application data layer | Only acme's 3 pending rows are returned; globex's rows are filtered out. |
| 2 | Set the tenant context to globex and run the same query | Only globex's 4 pending rows are returned. |
| 3 | Attempt to fetch a known globex request id while in the acme context (without `IgnoreQueryFilters`) | No row is returned -- the global filter excludes it. |
| 4 | Verify the pending-queue handler does not call `IgnoreQueryFilters()` | The query relies on the global filter; isolation is not bypassed for the queue. |
| 5 | Verify writes are stamped, not client-supplied | Any TenantId on `leave_request` reflects the resolved context via `TenantInterceptor`, not a client value. |
| 6 | Note on RLS | The story text references "RLS"; in this codebase the equivalent guarantee is provided by EF global query filters + TenantInterceptor uniformly across entities (per vault). |

## 6. Postconditions
- No data mutated.
- The data layer prevents cross-tenant reads of pending requests regardless of query path.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
