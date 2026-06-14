---
id: TC-ATT-ISO-008
user_story: US-ATT-005
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-ISO-008: Shifts and assignments are tenant-isolated -- Tenant A shifts/employee_shift not visible to or actionable by Tenant B

## 1. Test Objective
Verify tenant isolation on the new `shift` and `employee_shift` tables (NFR-3, S10): an HR Officer authenticated in Tenant A cannot see, fetch, edit, delete, clone, or assign a Tenant B shift, and cannot create/read employee_shift assignments across tenants. Tenant B's shift is invisible (EF global query filter -> 404) and untouched; assign/create stamps tenant_id from the resolved context (TenantInterceptor), so body-injected tenant_id/employee_id cannot cross tenants; the shift-resolve endpoint never returns another tenant's shift. This is the shift-specific isolation TC; it extends the cross-cutting context/cache isolation of TC-ATT-ISO-001..004 to the shift surface.

## 2. Related Requirements
- User Story: US-ATT-005
- Non-Functional: NFR-3 (PostgreSQL RLS / tenant isolation on `shift` and `employee_shift`)
- Assumptions/Constraints: S10 (Tenant A shift definitions not visible to Tenant B)
- Functional Requirements: FR-2 (shift write tenant-scoped), FR-3 (assignment tenant-scoped), FR-7 (resolve tenant-scoped)

## 3. Preconditions
- Tenants "acme" and "globex" both `active`, Attendance module enabled.
- HR Officer "Priya" authenticated in acme with `Attendance.Shift.Manage`.
- "globex" has a shift "GX Night" (shift_id known) and an employee_shift assignment for a globex employee "Sam Doe" (ids known).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | acme | Priya |
| Target shift | globex "GX Night" shift_id | Tenant B shift |
| Spoofed body tenant_id | globex_tenant_id | attempt to scope into Tenant B |
| Spoofed employeeId | globex Sam Doe | attempt to assign across tenants |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya (acme), `GET /api/v1/attendance/shifts` | Only acme shifts are listed; "GX Night" does not appear (EF global query filter scopes by tenant_id). |
| 2 | As Priya, `GET/PUT/DELETE .../shifts/{globex_shift_id}` and `POST .../shifts/{globex_shift_id}/clone` | Each returns 404 (the globex row is invisible from acme's context); globex shift unchanged. |
| 3 | As Priya, `POST .../shifts/{globex_shift_id}/assign` with acme employeeIds | 404 -- the globex shift is not actionable from acme; no assignment created. |
| 4 | As Priya, create a shift with a body injecting `tenant_id = globex_tenant_id` | The injected tenant_id is ignored; the shift is stamped acme by TenantInterceptor; nothing is created under globex. |
| 5 | As Priya, assign an acme shift with a body including `employeeIds = [globex Sam Doe]` | The cross-tenant employee is not resolvable in acme's scope -> rejected/ignored; no employee_shift row links an acme shift to a globex employee. |
| 6 | As Priya, `GET .../employees/{globex Sam Doe}/shift?date=today` | 404/denied -- a Tenant A caller cannot resolve a Tenant B employee's shift; no globex shift detail leaks. |
| 7 | Send with `X-Tenant-Subdomain: globex` but an acme JWT | Tenant/claim mismatch is rejected (per TC-ATT-ISO-002); no cross-tenant read or write occurs. |
| 8 | Verify the database (both directions) | globex "GX Night" and Sam Doe's employee_shift are unchanged; repeat as a globex HR officer against an acme shift -- same isolation holds. (If RLS policies are later added on `shift`/`employee_shift`, assert a DB session set to acme cannot SELECT/UPDATE a globex row even via a direct query -- currently enforced via EF Core global query filters + TenantInterceptor, the NFR-3 RLS extension point.) |

## 6. Postconditions
- No cross-tenant shift read, edit, delete, clone, assignment, or resolve occurred; the other tenant's shift and assignment data remain untouched; tenant scope is always server-resolved.

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
- **EF query filters vs PostgreSQL RLS:** US-ATT-005 NFR-3/S10 name PostgreSQL RLS on `shift`/`employee_shift`; the platform currently enforces isolation via EF Core global query filters + TenantInterceptor. This TC (and the reused TC-ATT-ISO-001/002/003/004) describe the EF mechanism and mark the RLS session-level assertion as an extension point. Consistent with US-ATT-001/002/003/004. **Reported to caller.**
- **Redis shift-definition cache (NFR-4):** not assumed wired. The cross-cutting tenant-scoped cache-key isolation is covered by TC-ATT-ISO-004 (CONDITIONAL on Redis; DB-fallback verified). When the shift cache lands, assert its keys are tenant-scoped. **Reported to caller.**
