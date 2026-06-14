---
id: TC-ATT-ISO-009
user_story: US-ATT-006
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-ISO-009: Overtime records are tenant-isolated -- Tenant A overtime is not visible to, approvable by, or reportable across Tenant B

## 1. Test Objective
Verify tenant isolation on the new `overtime_record` table (NFR-2, S10): an actor authenticated in Tenant A cannot read, list (own/pending), approve, reject, adjust, pre-approve into, or report on a Tenant B overtime record; the cross-tenant row is invisible (EF global query filter -> 404) and untouched; pre-approval/decision writes stamp tenant_id from the resolved context (TenantInterceptor), so body-injected tenant_id/employee_id cannot cross tenants; the monthly report and pending queue never return another tenant's overtime. This is the overtime-specific isolation TC; it extends the cross-cutting context/cache isolation of TC-ATT-ISO-001..004 to the overtime surface.

## 2. Related Requirements
- User Story: US-ATT-006
- Non-Functional: NFR-2 (PostgreSQL RLS / tenant isolation on overtime records)
- Assumptions/Constraints: S10 (multi-tenant RLS isolates overtime records per tenant)
- Functional Requirements: FR-2 (overtime_record write tenant-scoped), FR-5/FR-6 (approval tenant-scoped), AC-5 (report tenant-scoped)

## 3. Preconditions
- Tenants "acme" and "globex" both active, Attendance module enabled.
- Manager "Ben" and HR "Priya" authenticated in acme; "globex" has a PENDING overtime_record (overtime_id known) for a globex employee "Sam Doe" (ids known) and a globex manager "Gina".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | acme | Ben (manager) / Priya (HR) |
| Target overtime | globex overtime_id | Tenant B record |
| Spoofed body tenant_id | globex_tenant_id | attempt to scope into Tenant B |
| Spoofed employeeId | globex Sam Doe | attempt to act across tenants |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Ben (acme), `GET /api/v1/attendance/overtime/pending` | Only acme team overtime is listed; globex's PENDING record does not appear (EF global query filter scopes by tenant_id). |
| 2 | As Ben, `POST /overtime/{globexOvertimeId}/approve` and `/reject` | Each returns 404 (the globex row is invisible from acme's context); the globex record is unchanged (still PENDING). |
| 3 | As an acme employee, `GET /overtime/my` | Returns only own acme overtime; no globex record by any id. |
| 4 | As an acme employee, `POST /overtime/pre-approval` with a body injecting `tenant_id = globex_tenant_id` / `employeeId = Sam Doe` | The injected tenant_id/employeeId are ignored; the pre-approval is stamped acme + the acting employee by TenantInterceptor; nothing is created under globex. |
| 5 | As Priya (acme HR), `GET /overtime/report?month=YYYY-MM` | The report contains only acme employees' overtime; no globex employee or minutes appear. |
| 6 | Send with `X-Tenant-Subdomain: globex` but an acme JWT | Tenant/claim mismatch is rejected (per TC-ATT-ISO-002); no cross-tenant read or write occurs. |
| 7 | Verify the database (both directions) | The globex overtime_record is unchanged; repeat as globex Gina/HR against an acme overtime record -- same isolation holds. (If RLS policies are later added on `overtime_record`, assert a DB session set to acme cannot SELECT/UPDATE a globex row even via a direct query -- currently enforced via EF Core global query filters + TenantInterceptor, the NFR-2 RLS extension point.) |

## 6. Postconditions
- No cross-tenant overtime read, approve, reject, adjust, pre-approval, or report occurred; the other tenant's overtime data remains untouched; tenant scope is always server-resolved.

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
- **EF query filters vs PostgreSQL RLS:** US-ATT-006 NFR-2/S10 name PostgreSQL RLS on overtime records; the platform currently enforces isolation via EF Core global query filters + TenantInterceptor. This TC (and the reused TC-ATT-ISO-001..004) describe the EF mechanism and mark the RLS session-level assertion as an extension point. Consistent with US-ATT-001..005. **Reported to caller.**
- The cross-cutting tenant-scoped cache-key isolation is covered by TC-ATT-ISO-004 (CONDITIONAL on Redis; DB-fallback verified). If an overtime/balance cache lands, assert its keys are tenant-scoped.
