---
id: TC-ATT-ISO-011
user_story: US-ATT-008
module: Attendance
priority: critical
type: security
status: fail
created: 2026-06-14
---

# TC-ATT-ISO-011: Late/early data is tenant-isolated -- the late_policy and per-record late/early flags, the report, and my-score never expose or act on another tenant's data

## 1. Test Objective
Verify tenant isolation on the US-ATT-008 surface (NFR-2, §10): the tenant-level `late_policy` and the late/early flags on `attendance_log` are scoped per tenant; an HR Officer in Tenant A cannot read or update Tenant B's late_policy, cannot see Tenant B employees in the late/early report, and cannot read Tenant B's my-score/late counts. The late_policy PUT cannot write into Tenant B (tenant_id is server-resolved, not body-supplied), and a subdomain/JWT mismatch is rejected. Extends the cross-cutting context/cache isolation of TC-ATT-ISO-001..004 to the late/early surface.

## 2. Related Requirements
- User Story: US-ATT-008
- Non-Functional: NFR-2 (PostgreSQL RLS / tenant isolation on attendance records used for late/early tracking)
- Data: late_policy (tenant_id, RLS-enforced per S7), attendance_log late/early fields
- Assumptions: §10 (multi-tenant RLS ensures late tracking data isolated per tenant)
- APIs: GET/PUT /late-policy; GET /late-early/report; GET /late-early/my-score

## 3. Preconditions
- Tenants "acme" and "globex" both active, Attendance module enabled.
- HR Officer "Priya" authenticated in acme. globex has its own late_policy and globex employees with seeded late/early records (a globex employeeId + a globex HR "Gloria" are known).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | acme | Priya (HR) |
| Target | globex late_policy + globex employees | Tenant B data |
| Spoofed body tenant_id | globex_tenant_id | attempt to write/scope into Tenant B |
| Spoofed employeeId | globex employee | attempt cross-tenant report/score |
| month / period | current | report + my-score window |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya (acme), `GET /api/v1/attendance/late-policy` | Returns acme's late_policy only; globex's policy is never returned (EF global query filter scopes by tenant_id). |
| 2 | As Priya, `PUT /late-policy` with body injecting `tenant_id = globex_tenant_id` | The injected tenant_id is ignored; the write applies to acme's policy only (TenantInterceptor stamps acme); globex's policy is unchanged. |
| 3 | As Priya, `GET /late-early/report?from=...&to=...&scope=all` | Only acme employees with their late/early counts appear; no globex employee row is present. |
| 4 | As Priya, `GET /late-early/report?...&employeeId=<globexEmployeeId>` | The globex id resolves to no rows (invisible from acme's context); no globex late/early data is returned. |
| 5 | As Priya, `GET /late-early/my-score` while attempting to target a globex employee id | Returns acme-context data only; no globex score/late count is exposed. |
| 6 | Send with `X-Tenant-Subdomain: globex` but an acme JWT | Tenant/claim mismatch is rejected (per TC-ATT-ISO-002); no cross-tenant read or write occurs. |
| 7 | Verify the tenant-scoped cache key | Any late-score/late-count cache key is tenant-scoped so acme and globex never collide -- CONDITIONAL on Redis; DB-fallback isolation verified now (reuses TC-ATT-ISO-004). |
| 8 | Verify the database / detection path (both directions) | Late/early detection stamps the tenant on the attendance_log via TenantInterceptor; acme's flags never appear under globex and vice versa (repeat as globex Gloria against acme). If RLS policies are later added on `late_policy`/`attendance_log`, assert a DB session set to acme cannot SELECT a globex late_policy or late/early row via a direct query -- currently enforced via EF Core global query filters + TenantInterceptor, the NFR-2 RLS extension point. |

## 6. Postconditions
- No cross-tenant late_policy read/write, report read, or score read occurred; the other tenant's late/early data and policy remain untouched; tenant scope is always server-resolved.

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
- **EF query filters vs PostgreSQL RLS:** US-ATT-008 NFR-2/§10 name PostgreSQL RLS on the late/early records (and late_policy.tenant_id is "RLS-enforced" per S7); the platform currently enforces isolation via EF Core global query filters + TenantInterceptor. This TC (and the reused TC-ATT-ISO-001..004) describe the EF mechanism and mark the RLS session-level assertion as an extension point. Consistent with US-ATT-001..007. **Reported to caller.**
- The cross-cutting tenant-scoped cache-key isolation is covered by TC-ATT-ISO-004 (CONDITIONAL on Redis; DB-fallback verified). late_policy is a NEW table introduced by this story; this single dedicated ISO TC covers its read + write isolation plus the late/early report + my-score + per-record flag isolation (one new ISO per story, reusing ISO-001..004, consistent with the module precedent).
