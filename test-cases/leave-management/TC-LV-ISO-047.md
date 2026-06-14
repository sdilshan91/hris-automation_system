---
id: TC-LV-ISO-047
user_story: US-LV-012
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-047: EF global query filters block cross-tenant aggregation in report queries (NFR-3, §7)

## 1. Test Objective
Verify that the report aggregation queries (over `leave_request`, `leave_ledger`, `leave_type`, `employee`, `department`, `job_title`) are constrained by the EF Core global query filters so that GROUP BY / SUM / COUNT / AVG aggregations can never span tenants — even via a join or a raw aggregation — confirming the "RLS-equivalent" isolation per the vault.

## 2. Related Requirements
- User Story: US-LV-012
- Non-Functional Requirements: NFR-3
- Data Requirements: §7 (source tables, aggregations)
- Note: isolation enforced by EF global query filters + TenantInterceptor (not Postgres RLS) per docs/vault/modules/leave-management.md.

## 3. Preconditions
- Tenants "acme" and "globex", each with employees, leave_request, and leave_ledger rows for overlapping leave types and date ranges.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tables | leave_request, leave_ledger, employee, department, job_title | §7 sources |
| Aggregations | SUM days, COUNT requests, AVG utilization | GROUP BY dept/type/month |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With the acme tenant context, execute a report's aggregation query | Every joined table is filtered by `TenantId == acme`; globex rows are excluded from the SUM/COUNT/AVG. |
| 2 | Compare the aggregate to a globex-context run | The two tenants produce independent totals; neither includes the other's rows. |
| 3 | Attempt an aggregation that joins employee→department→leave_request | The join cannot pull a globex department/employee into acme's result (filters applied to each entity). |
| 4 | If a materialized view / DB view backs the aggregation (§7) | Confirm the view is itself tenant-filtered (or queried with the tenant predicate); record CONDITIONAL/DEFERRED if such a view is not yet built, with the live-query isolation verified. |

## 6. Postconditions
- Report aggregations are tenant-bounded by EF global query filters; no cross-tenant SUM/COUNT/AVG is possible.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
