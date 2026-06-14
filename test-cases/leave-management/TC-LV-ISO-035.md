---
id: TC-LV-ISO-035
user_story: US-LV-009
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-035: EF Core global query filters block cross-tenant access to leave_request/employee rows feeding the calendar (NFR-2)

## 1. Test Objective
Verify the read-isolation layer: the team-calendar aggregation (leave_request joined with employee and leave_type) is constrained by the EF Core global query filter (`TenantId == _tenantContext.TenantId`), so a query executed under Tenant B's context can never materialize Tenant A's rows. (Tenant isolation in this codebase is EF global query filters + TenantInterceptor, not Postgres RLS -- per docs/vault/modules/leave-management.md.)

## 2. Related Requirements
- User Story: US-LV-009
- Non-Functional Requirements: NFR-2
- Data Requirements: Section 7 (leave_request joined with employee and leave_type)
- Note: Story says "RLS"; the implemented mechanism is EF global query filters + TenantInterceptor.

## 3. Preconditions
- Tenants "acme" and "globex" each with leave_request + employee rows in the same date range.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Filter | lr => lr.TenantId == _tenantContext.TenantId | EF global query filter |
| Join | leave_request x employee x leave_type | all tenant-scoped entities |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Execute the calendar aggregation query under acme's tenant context | Only acme leave_request/employee rows are returned; globex rows are filtered out at the DB query level. |
| 2 | Attempt to reference a globex leave_request id directly under acme context | The row is not materialized (filtered out); no cross-tenant read. |
| 3 | Verify the join does not bypass the filter | The employee and leave_type joins are themselves tenant-filtered, so no leak occurs via a join onto another tenant's rows. |
| 4 | Confirm IgnoreQueryFilters is not used on this path | The calendar query does not call `IgnoreQueryFilters()`; isolation is intact. |

## 6. Postconditions
- The EF global query filter blocks cross-tenant rows for every entity feeding the calendar aggregation.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [x] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
