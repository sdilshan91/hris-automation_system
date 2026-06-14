---
id: TC-ATT-ISO-010
user_story: US-ATT-007
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-ISO-010: Monthly attendance summaries are tenant-isolated -- Tenant A's summary, drill-down, generation, and export never include or act on Tenant B data

## 1. Test Objective
Verify tenant isolation on the attendance_monthly_summary surface (NFR-3, §10): an HR Officer in Tenant A cannot see, drill into, generate, or export another tenant's monthly summary. Tenant A's summary list contains only Tenant A employees; a Tenant B employee drill-down is invisible (EF global query filter -> 404); on-demand generation and the Hangfire batch job scope to the resolved tenant context so no cross-tenant rows are written; exports contain only the acting tenant's data; and the summary/balance cache key (`att_summary:{tenant_id}:{year_month}:{employee_id}`) is tenant-scoped. Extends the cross-cutting context/cache isolation of TC-ATT-ISO-001..004 to the summary surface.

## 2. Related Requirements
- User Story: US-ATT-007
- Non-Functional: NFR-3 (PostgreSQL RLS / tenant isolation -- HR in Tenant A cannot see Tenant B summaries)
- Assumptions/Constraints: §10 (isolation enforced at the application layer -- tenant context in the Hangfire job -- and the database layer)
- Functional Requirements: FR-3 (summary rows tenant-scoped), FR-5 (filters tenant-scoped), FR-6/FR-7 (export tenant-scoped), FR-8 (cache key tenant-scoped)

## 3. Preconditions
- Tenants "acme" and "globex" both active, Attendance module enabled.
- HR Officer "Priya" authenticated in acme. globex has generated monthly summaries (2026-05) for globex employees (a globex employeeId is known) and a globex HR "Gloria".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | acme | Priya (HR, Attendance.Read.All) |
| Target drill-down | globex employeeId | Tenant B record |
| Spoofed body/query tenant_id | globex_tenant_id | attempt to scope into Tenant B |
| Spoofed employeeId | globex employee | attempt cross-tenant drill-down |
| month | 2026-05 | selected period |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya (acme), `GET /api/v1/attendance/summary/monthly?month=2026-05` | Only acme employees appear; no globex employee row is present (EF global query filter scopes by tenant_id). |
| 2 | As Priya, `GET /summary/monthly/{globexEmployeeId}?month=2026-05` | 404 (the globex row is invisible from acme's context); no globex day-by-day data is returned. |
| 3 | As Priya, `POST /summary/monthly/generate?month=2026-05` with a body/query injecting `tenant_id = globex_tenant_id` | The injected tenant_id is ignored; generation runs for acme only (resolved tenant context); no globex summary rows are created or overwritten. |
| 4 | As Priya, `GET /summary/monthly/export?month=2026-05&format=xlsx` (and with an injected globex departmentId/employeeId) | The export contains only acme employees; injected globex filter ids return no globex rows. |
| 5 | Send with `X-Tenant-Subdomain: globex` but an acme JWT | Tenant/claim mismatch is rejected (per TC-ATT-ISO-002); no cross-tenant read, generation, or export occurs. |
| 6 | Verify the cache key (FR-8) | The summary cache key is tenant- and employee-scoped (`att_summary:{tenant_id}:{year_month}:{employee_id}`), so acme and globex never collide -- CONDITIONAL on Redis; DB/materialized-table fallback isolation verified now (reuses TC-ATT-ISO-004). |
| 7 | Verify the database / batch path (both directions) | The Hangfire summary job writes only the acting tenant's rows (no acme employee in globex summary, and vice versa); repeat as globex Gloria against an acme summary -- same isolation holds. (If RLS policies are later added on attendance_monthly_summary, assert a DB session set to acme cannot SELECT a globex summary row even via a direct query -- currently enforced via EF Core global query filters + TenantInterceptor, the NFR-3 RLS extension point.) |

## 6. Postconditions
- No cross-tenant summary read, drill-down, generation, or export occurred; the other tenant's summary data remains untouched; tenant scope is always server-resolved at both the request and batch-job layers.

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
- **EF query filters vs PostgreSQL RLS:** US-ATT-007 NFR-3/§10 name PostgreSQL RLS on attendance_monthly_summary; the platform currently enforces isolation via EF Core global query filters + TenantInterceptor (and the tenant context inside the Hangfire job). This TC (and the reused TC-ATT-ISO-001..004) describe the EF mechanism and mark the RLS session-level assertion as an extension point. Consistent with US-ATT-001..006. **Reported to caller.**
- The cross-cutting tenant-scoped cache-key isolation is covered by TC-ATT-ISO-004 (CONDITIONAL on Redis; DB-fallback verified). The summary key `att_summary:{tenant_id}:{year_month}:{employee_id}` already embeds tenant_id by design.
- §10 explicitly requires the Hangfire batch job to use tenant-scoped queries -- Step 7 verifies the batch-processing side; TC-ATT-096 verifies the per-tenant job scoping in the functional flow.
