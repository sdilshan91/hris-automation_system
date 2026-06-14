---
id: TC-ATT-ISO-007
user_story: US-ATT-004
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-ISO-007: Approval is tenant-isolated -- a manager in Tenant A cannot see, approve, or reject a regularization in Tenant B

## 1. Test Objective
Verify tenant isolation on the approval surface (NFR-3): a manager authenticated in Tenant A cannot see a Tenant B regularization in their approval queue, cannot fetch a Tenant B regularization by id, and cannot approve/reject/bulk-approve a Tenant B request (by id, by body-injected tenant_id/employee_id, or by subdomain/JWT switch). Tenant B's record is invisible (EF global query filter -> 404) and remains untouched; the approve action never writes a Tenant B attendance_log; the server-resolved tenant context always governs. This is the approval-specific isolation TC; it extends the table-level isolation of TC-ATT-ISO-001..004 (read / missing-context / write-stamping / cache) and the regularization read+submit isolation of TC-ATT-ISO-006 to the approve/reject mutations.

## 2. Related Requirements
- User Story: US-ATT-004
- Non-Functional: NFR-3 (PostgreSQL RLS / tenant isolation -- managers only see requests within their tenant)
- Functional Requirements: FR-2 (attendance_log write tenant-scoped), FR-7 (relationship/scope checks), FR-1 (queue scope)
- Assumptions/Constraints: S10 (multi-tenant isolation -- Tenant A managers cannot see/act on Tenant B requests)

## 3. Preconditions
- Tenants "acme" and "globex" both `active`, Attendance module enabled, approval workflow configured in each.
- Manager "Dana Wells" authenticated in acme with `Attendance.Approve.Team`.
- "globex" has a PENDING `attendance_regularization` (UUID known) for an employee "Sam Doe" who reports to a globex manager; and an existing globex `attendance_log` (UUID known).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | acme | Dana Wells |
| Target regularization | globex regularization_id | Sam Doe's PENDING request |
| Spoofed body tenant_id | globex_tenant_id | attempt to scope into Tenant B |
| Spoofed body employee_id | globex employee (Sam Doe) | attempt to target Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana (acme), open the approval queue | Only acme direct-report PENDING rows appear; zero globex rows (Sam Doe's request is invisible). |
| 2 | As Dana, `GET .../regularizations/{globex_regularization_id}` | Response 404 (EF global query filter excludes the globex row from acme's context); details never returned. |
| 3 | As Dana, `POST .../regularizations/{globex_regularization_id}/approve` | Response 404/403 -- the globex request is not visible/actionable from acme; status stays PENDING in globex; no acme-side write. |
| 4 | As Dana, reject the globex request with a valid reason | Same isolation -- refused; globex request unchanged. |
| 5 | As Dana, bulk-approve a set that includes the globex id | The globex id is treated as not found/denied; no globex row is approved; eligible acme items (if any) are unaffected. |
| 6 | As Dana, approve with a body injecting `tenant_id = globex_tenant_id` / `employee_id = globex` | The injected values are ignored; the server uses acme's resolved context; no globex regularization or attendance_log is created/modified. |
| 7 | Send with `X-Tenant-Subdomain: globex` but an acme JWT | Tenant/claim mismatch is rejected (per TC-ATT-ISO-002); no cross-tenant read or decision occurs. |
| 8 | Verify the database (both directions) | Sam Doe's globex regularization stays PENDING and its attendance_log unchanged; repeat as a globex manager against an acme request -- same isolation holds. (If RLS policies are later added on `attendance_regularization`/`attendance_log`, assert a DB session set to acme cannot SELECT/UPDATE a globex row even via a direct query -- currently enforced via EF Core global query filters + TenantInterceptor, the NFR-3 RLS extension point.) |

## 6. Postconditions
- No cross-tenant read, approval, rejection, or attendance_log write occurred; the other tenant's regularization and attendance data remain untouched; tenant scope is always server-resolved.

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
- **EF query filters vs PostgreSQL RLS:** US-ATT-004 NFR-3/S10 name PostgreSQL RLS; the platform currently enforces isolation via EF Core global query filters + TenantInterceptor. This TC (and the reused TC-ATT-ISO-001/003/006) describe the EF mechanism and mark the RLS session-level assertion as an extension point. Consistent with US-ATT-001/002/003. **Reported to caller.**
