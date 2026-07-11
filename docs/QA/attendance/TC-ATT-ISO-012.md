---
id: TC-ATT-ISO-012
user_story: US-ATT-009
module: Attendance
priority: critical
type: security
status: fail
created: 2026-06-15
---

# TC-ATT-ISO-012: Payroll-integration data is tenant-isolated -- payroll-data, period-lock, and reconciliation never expose or act on another tenant's attendance; payroll in Tenant A cannot read Tenant B attendance

## 1. Test Objective
Verify tenant isolation across the US-ATT-009 payroll-integration surface (NFR-3, §10): an HR Officer / payroll caller in Tenant A cannot pull Tenant B's payroll-data, cannot read or create/unlock a period-lock in Tenant B, and cannot see Tenant B employees in the reconciliation view. tenant_id on `attendance_period_lock` and on all summary/overtime/log data is server-resolved (not body-supplied), and a subdomain/JWT mismatch is rejected. Extends the cross-cutting context/cache isolation of TC-ATT-ISO-001..004 to the payroll-integration surface.

## 2. Related Requirements
- User Story: US-ATT-009
- Non-Functional: NFR-3 (PostgreSQL RLS enforces tenant isolation on all attendance data accessed by the payroll module)
- Data: §7 attendance_period_lock (tenant_id, FK, RLS-enforced); payroll-data sourced from tenant-scoped summary/overtime/log
- Assumptions: §10 (multi-tenant RLS ensures the payroll module only accesses the current tenant's attendance data)
- APIs: GET /payroll-data; GET/POST /period-lock; POST /period-lock/{id}/unlock; GET /reconciliation

## 3. Preconditions
- Tenants "acme" and "globex" both active, Attendance + Payroll enabled.
- HR Officer "Priya" authenticated in acme. globex has its own generated summary, its own employees with attendance, and its own period-lock (a globex employeeId, a globex lock_id, and a globex HR "Gloria" are known).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | acme | Priya (HR) |
| Target | globex payroll-data + globex period-lock + globex employees | Tenant B data |
| Spoofed body tenant_id | globex_tenant_id | attempt to lock/scope into Tenant B |
| Spoofed employeeIds | globex employees | attempt cross-tenant pull/reconciliation |
| Spoofed lock_id | globex lock_id | attempt cross-tenant unlock |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya (acme), `GET /payroll-data?month=2026-05&employeeIds=<globexEmployeeIds>` | Globex ids resolve to no rows (invisible from acme's context); no globex attendance/payroll-data returned (EF global query filter scopes by tenant_id). |
| 2 | As Priya, `GET /payroll-data?month=2026-05` (whole period) | Only acme employees returned; no globex employee row present. |
| 3 | As Priya, `POST /period-lock` with body injecting `tenant_id = globex_tenant_id` | The injected tenant_id is ignored; the lock applies to acme only (TenantInterceptor stamps acme); globex has no lock created by this call. |
| 4 | As Priya, `GET /period-lock?month=2026-05` | Returns only acme's lock; globex's lock is never returned. |
| 5 | As Priya, `POST /period-lock/{globexLockId}/unlock` | Not found / forbidden -- the globex lock is invisible/untouchable from acme; globex's lock remains unchanged. |
| 6 | As Priya, `GET /reconciliation?month=2026-05` | Only acme employees + acme attendance appear; no globex row. |
| 7 | Send with `X-Tenant-Subdomain: globex` but an acme JWT | Tenant/claim mismatch rejected (per TC-ATT-ISO-002); no cross-tenant read or write occurs. |
| 8 | Verify the tenant-scoped cache key | Any payroll-data/reconciliation cache key is tenant-scoped so acme and globex never collide -- CONDITIONAL on Redis; DB-fallback isolation verified now (reuses TC-ATT-ISO-004). |
| 9 | Verify the database / lock path (both directions) | period-lock rows are stamped with tenant_id via TenantInterceptor; acme's lock never appears under globex and vice versa (repeat as globex Gloria against acme). If RLS policies are later added on `attendance_period_lock` / the summary/log tables, assert a DB session set to acme cannot SELECT a globex lock or attendance row via a direct query -- currently enforced via EF Core global query filters + TenantInterceptor, the NFR-3 RLS extension point. |

## 6. Postconditions
- No cross-tenant payroll-data pull, period-lock read/create/unlock, or reconciliation read occurred; the other tenant's attendance, locks, and payroll inputs remain untouched; tenant scope is always server-resolved.

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
- **EF query filters vs PostgreSQL RLS:** US-ATT-009 NFR-3/§10 name PostgreSQL RLS enforcing isolation on all attendance data the payroll module accesses; the platform currently enforces isolation via EF Core global query filters + TenantInterceptor. This TC (and the reused TC-ATT-ISO-001..004) describe the EF mechanism and mark the RLS session-level assertion as an extension point. Consistent with US-ATT-001..008. **Reported to caller.**
- `attendance_period_lock` is a NEW table introduced by this story; this single dedicated ISO TC covers its read + create + unlock isolation plus the payroll-data + reconciliation isolation (one new ISO per story, reusing ISO-001..004 for the cross-cutting context/cache mechanism, consistent with the module precedent). The cross-tenant cache-key isolation is covered by TC-ATT-ISO-004 (CONDITIONAL on Redis; DB-fallback verified).
- This TC is the concrete realisation of the "payroll in Tenant A cannot read Tenant B attendance" requirement -- the central concern as attendance becomes a data SOURCE for the payroll module.
