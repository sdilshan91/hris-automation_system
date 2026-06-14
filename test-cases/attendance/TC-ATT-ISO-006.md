---
id: TC-ATT-ISO-006
user_story: US-ATT-003
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-ISO-006: Regularization is tenant-isolated -- an employee in Tenant A cannot see or submit a regularization for Tenant B

## 1. Test Objective
Verify tenant isolation on the regularization feature (NFR-2): an authenticated employee in Tenant A cannot read Tenant B's `attendance_regularization` records, cannot create a regularization in Tenant B (by body-injected `tenant_id`/`employee_id` or by linking to a Tenant B `attendance_log_id`), cannot target a Tenant B approver, and a subdomain/JWT mismatch is rejected. Tenant B's records are invisible (EF global query filter -> 404) and untouched; the server-resolved tenant context always governs. This is the regularization-specific isolation TC; it extends the table-level isolation already established by TC-ATT-ISO-001 (read), TC-ATT-ISO-002 (missing/mismatched tenant context), TC-ATT-ISO-003 (write stamping), and TC-ATT-ISO-004 (cache scoping) to the `attendance_regularization` table and the submit mutation.

## 2. Related Requirements
- User Story: US-ATT-003
- Non-Functional Requirements: NFR-2 (PostgreSQL RLS / tenant isolation on attendance_regularization)
- Functional Requirements: FR-2 (tenant_id/employee_id from session context, not client input)
- Assumptions/Constraints: S10 (multi-tenant RLS isolates regularization records per tenant)

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both exist and are `active`, Attendance module enabled, regularization workflow configured in each.
- Employee "Jordan Lee" is authenticated in "acme" with `Attendance.Regularize.Self`.
- Tenant "globex" has: an employee "Sam Doe", a PENDING `attendance_regularization` (UUID known to the test), and an open `attendance_log` (UUID known) -- used to attempt cross-tenant read/link.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth Context | acme | Jordan Lee |
| Target regularization | globex regularization_id | Sam Doe's PENDING request |
| Target log to link | globex attendance_log_id | For cross-tenant link attempt |
| Spoofed body tenant_id | globex_tenant_id | Attempt to scope into Tenant B |
| Spoofed body employee_id | globex_employee_id (Sam Doe) | Attempt to submit for Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jordan Lee (acme), `GET /api/v1/attendance/regularizations/{globex_regularization_id}` | Response 404 Not Found (EF global query filter excludes the globex row from acme's context); the globex record's details are never returned. |
| 2 | List regularizations as Jordan Lee | Only acme regularizations are returned; zero globex rows appear. |
| 3 | Submit a regularization with a body injecting `tenant_id = globex_tenant_id` | The injected tenant_id is ignored; the row (if created) is stamped with acme's `tenant_id` by the TenantInterceptor. No globex row is created. |
| 4 | Submit with a body injecting `employee_id = globex_employee_id` (Sam Doe) | The server uses the authenticated employee (Jordan Lee, acme); no regularization is created for Sam Doe / globex. |
| 5 | Submit a MISSED_CLOCK_OUT regularization linking `attendance_log_id` = the globex log UUID | The globex log is not visible in acme's context (filtered out); the link is rejected/treated as not found. No cross-tenant link is created. |
| 6 | Send with `X-Tenant-Subdomain: globex` but an acme JWT | Tenant/claim mismatch is rejected (per TC-ATT-ISO-002); no cross-tenant read or write occurs. |
| 7 | Verify the database | Sam Doe's globex regularization and log are unchanged; no acme-initiated row carries globex's tenant_id; tenant scope is always server-resolved. |
| 8 | Verify both directions | Repeat as a globex user against an acme regularization -- same isolation holds. (If RLS policies are later added on `attendance_regularization`, assert a DB session set to acme cannot SELECT/INSERT/UPDATE a globex row even via a direct query -- currently enforced via EF Core global query filters + TenantInterceptor, per the NFR-2 RLS extension point.) |

## 6. Postconditions
- No cross-tenant read, write, link, or approver-targeting occurred; the other tenant's regularization data remains untouched; tenant scope is always server-resolved.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
