---
id: TC-PRF-ISO-027
user_story: US-PRF-007
module: Performance Management
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-PRF-ISO-027: Materialized-view aggregates + refresh are tenant-derived (server-side tenant_id, no cross-tenant aggregate leakage / injection)

## 1. Test Objective
Verify NFR-2 + NFR-3: the dashboard's aggregate computations and the performance_summary materialized-view refresh derive tenant_id from the resolved server-side tenant context (NOT from any client-supplied body/param/filter), so a caller cannot cause an aggregate to span tenants. A tenant filter cannot inject a foreign tenant's cycle/department/employee into the GROUP BY, and the refresh writes only the acting tenant's summary rows.

## 2. Related Requirements
- User Story: US-PRF-007
- Non-Functional Requirements: NFR-2, NFR-3
- Data Requirements: S7 (performance_summary materialized view; tenant_id scoped)

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both have populated performance_summary views.
- An HR Officer with `Performance.Read.All` authenticated in acme; known globex cycle/department/employee ids.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Acting tenant | acme | server-derived tenant_id |
| Foreign ids | globex cycleId / deptId / employeeId | injection probes |
| Refresh job | acme materialized-view refresh | tenant-scoped write |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, send a dashboard request with a body/param attempting to set `tenantId=globex` (or a tenant override) | The server IGNORES any client tenant_id and uses the resolved acme context; the aggregate is over acme only (NFR-2). |
| 2 | As acme HR, filter `departmentId={globex_deptId}` / `cycleId={globex_cycleId}` | The foreign ids resolve to nothing within acme's filtered view; no globex rows enter the aggregate; result is empty for that filter, never a cross-tenant blend. |
| 3 | Inspect the aggregate query | The GROUP BY / WHERE includes `tenant_id = acme` (server-derived); no client value participates in the tenant predicate. |
| 4 | Trigger the materialized-view refresh in acme context | The refresh recomputes and writes ONLY acme's performance_summary rows; globex rows are untouched (tenant-scoped write). |
| 5 | Verify at the DB level after refresh | `performance_summary` rows for globex are unchanged (same checksum/row count); acme rows reflect the refresh. |
| 6 | Repeat from globex attempting to reference acme ids | Symmetric: globex context cannot aggregate or refresh acme data. |

## 6. Postconditions
- Aggregates and materialized-view refreshes are strictly tenant-derived; no client input can cause cross-tenant aggregation or write.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
