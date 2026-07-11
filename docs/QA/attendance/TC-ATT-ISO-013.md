---
id: TC-ATT-ISO-013
user_story: US-ATT-010
module: Attendance
priority: critical
type: security
status: fail
created: 2026-06-15
---

# TC-ATT-ISO-013: Dashboard / live-board / reports / trends / scheduled-config are tenant-isolated -- Tenant A's HR never sees or aggregates Tenant B attendance; scheduled_report_config is tenant-scoped

## 1. Test Objective
Verify tenant isolation across the US-ATT-010 dashboard + reporting surface (NFR-4, §10): an HR Officer in Tenant A cannot see Tenant B employees or attendance in the dashboard KPIs, live board, department comparison, custom report, or trends; cannot read/create/update/delete a Tenant B `scheduled_report_config`; and aggregate metrics (counts, rates, trends) NEVER sum across tenants. tenant scope is server-resolved (not body/query-supplied), and a subdomain/JWT mismatch is rejected. Extends the cross-cutting context/cache isolation of TC-ATT-ISO-001..004 to the reporting surface.

## 2. Related Requirements
- User Story: US-ATT-010
- Non-Functional: NFR-4 (PostgreSQL RLS enforces tenant isolation on all dashboard and report data)
- Data: §7 scheduled_report_config (tenant_id, FK, RLS-enforced); dashboard KPIs keyed att_dashboard:{tenant_id}:...
- Assumptions: §10 (multi-tenant RLS isolates dashboard + report data per tenant)
- APIs: GET /dashboard, /dashboard/live-board, /reports/department-comparison, /reports/custom (+ /export), /reports/trends, GET/POST/PUT/DELETE /reports/scheduled

## 3. Preconditions
- Tenants "acme" and "globex" both active, Attendance enabled.
- HR Officer "Priya" authenticated in acme. globex has its own employees with attendance, generated summaries, and a globex scheduled_report_config (a globex employeeId, a globex departmentId, and a globex config_id are known).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | acme | Priya (HR, Reports.View.All) |
| Target | globex dashboard / reports / employees / config | Tenant B data |
| Spoofed query tenant_id | globex_tenant_id | attempt to scope into Tenant B |
| Spoofed employeeIds / departmentId | globex employee / department | attempt cross-tenant report rows |
| Spoofed config_id | globex config_id | attempt cross-tenant config read/update/delete |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya (acme), `GET /dashboard` | KPIs computed over acme employees ONLY; the expected/clocked-in/on-leave/absent counts never include globex employees (EF global query filter scopes by tenant_id). |
| 2 | As Priya, `GET /dashboard/live-board` | only acme employees listed; no globex employee row appears. |
| 3 | As Priya, `GET /reports/department-comparison?month=2026-05` | only acme departments + rates; a globex department is never returned, and no rate aggregates across tenants. |
| 4 | As Priya, `GET /reports/custom?...&employeeIds=<globexEmployeeIds>&departmentId=<globexDepartmentId>` | the globex ids/department resolve to no rows (invisible from acme's context); no globex attendance returned. |
| 5 | As Priya, `GET /reports/trends?months=12` | every monthly point is computed from acme summaries only; globex months never contribute (no cross-tenant SUM/AVG). |
| 6 | As Priya, `GET /reports/scheduled` | only acme configs listed; the globex config is never returned. |
| 7 | As Priya, `POST /reports/scheduled` injecting `tenant_id = globex_tenant_id` in the body | the injected tenant_id is ignored; the config is stamped to acme (TenantInterceptor); globex gets no config from this call. |
| 8 | As Priya, `PUT/DELETE /reports/scheduled/{globexConfigId}` | not found / forbidden -- the globex config is invisible/untouchable from acme; it remains unchanged. |
| 9 | Export: as Priya, `GET /reports/custom/export?...` | the exported CSV/XLSX/PDF contains acme rows only -- no globex row in any format. |
| 10 | Send with `X-Tenant-Subdomain: globex` but an acme JWT | tenant/claim mismatch rejected (per TC-ATT-ISO-002); no cross-tenant read or write occurs. |
| 11 | Verify the tenant-scoped cache key | the dashboard KPI cache key is tenant-scoped (`att_dashboard:{tenant_id}:{date}:{metric}`, §7) so acme and globex never collide -- CONDITIONAL on Redis; DB-fallback isolation verified now (reuses TC-ATT-ISO-004). |
| 12 | Verify the database / aggregation path (both directions) | scheduled_report_config rows are stamped with tenant_id via TenantInterceptor; acme aggregates never read globex summary/log/overtime rows and vice versa (repeat as a globex HR against acme). If RLS policies are later added on the reporting source tables, assert a DB session set to acme cannot SELECT a globex attendance/summary/config row via a direct query -- currently enforced via EF Core global query filters + TenantInterceptor, the NFR-4 RLS extension point. |

## 6. Postconditions
- No cross-tenant dashboard read, report/aggregate, export, or scheduled-config read/create/update/delete occurred; aggregates are always single-tenant; tenant scope is server-resolved.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **EF query filters vs PostgreSQL RLS:** US-ATT-010 NFR-4/§10 name PostgreSQL RLS enforcing isolation on all dashboard + report data; the platform currently enforces isolation via EF Core global query filters + TenantInterceptor. This TC (and the reused TC-ATT-ISO-001..004) describe the EF mechanism and mark the RLS session-level assertion as an extension point. Consistent with US-ATT-001..009. **Reported to caller.**
- `scheduled_report_config` is a NEW table introduced by this story; this single dedicated ISO TC covers its read + create + update + delete isolation plus the dashboard / live-board / department-comparison / custom-report / trends / export aggregation isolation (one new ISO per story, reusing ISO-001..004 for the cross-cutting context/cache mechanism, consistent with the module precedent). The cross-tenant cache-key isolation reuses TC-ATT-ISO-004 (CONDITIONAL on Redis; DB-fallback verified).
- The critical isolation concern here is AGGREGATION: a KPI count, department rate, or trend must never sum a Tenant B row into a Tenant A figure -- the global query filter guarantees this at the source, verified in both directions.
