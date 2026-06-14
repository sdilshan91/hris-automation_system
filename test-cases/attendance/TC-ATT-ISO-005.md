---
id: TC-ATT-ISO-005
user_story: US-ATT-002
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-ISO-005: Clock-out is tenant-isolated — an employee in Tenant A cannot close Tenant B's open attendance record

## 1. Test Objective
Verify write-side tenant isolation on the clock-out path: an authenticated employee in Tenant A cannot clock out (close) an open `attendance_log` belonging to Tenant B, whether by passing Tenant B's `attendance_log_id`, by injecting a foreign `tenant_id`/`employee_id` in the body, or by switching the subdomain. The target record must be invisible (EF global query filter → 404) and untouched. This extends the table-level isolation already established by TC-ATT-ISO-001 (read), TC-ATT-ISO-002 (missing tenant context), TC-ATT-ISO-003 (write stamping), and TC-ATT-ISO-004 (cache scoping) to the specific clock-out mutation.

## 2. Related Requirements
- User Story: US-ATT-002
- Non-Functional Requirements: NFR-4 (RLS / tenant isolation on attendance_log)
- Functional Requirements: FR-1 (tenant_id from session context, not client input)
- Assumptions/Constraints: S10 (multi-tenant RLS scopes clock-out operations)

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both exist and are `active`, Attendance module enabled.
- Employee "Jordan Lee" is authenticated in "acme" with `Attendance.Clock.Self`.
- Employee "Sam Doe" in "globex" has ONE OPEN `attendance_log` (clock_in set, clock_out null). The UUID of that record is known to the test (to attempt cross-tenant closure).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth Context | acme | Jordan Lee |
| Target record | globex open attendance_log id | Sam Doe's open record |
| Spoofed body tenant_id | globex_tenant_id | Attempt to scope into Tenant B |
| Spoofed body employee_id | globex_employee_id | Attempt to close another tenant's record |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jordan Lee (acme), send `POST /api/v1/attendance/clock-out` targeting globex's open `attendance_log_id` | Response 404 Not Found (EF global query filter excludes the globex row from acme's context); never 200. The globex record is NOT closed. |
| 2 | Send a clock-out body injecting `tenant_id = globex_tenant_id` | The injected tenant_id is ignored; the resolved server-side context (acme) governs. No globex record is modified. |
| 3 | Send a clock-out body injecting `employee_id = globex_employee_id` | The server uses the authenticated employee (Jordan Lee, acme); it does not close globex's record. Request is rejected or scoped to acme (where Jordan has no matching open record → handled per TC-ATT-014). |
| 4 | Attempt with `X-Tenant-Subdomain: globex` but an acme JWT | Tenant/claim mismatch is rejected (per TC-ATT-ISO-002 sub-case D); no cross-tenant write. |
| 5 | Verify the database | Sam Doe's globex record is STILL OPEN (clock_out null, unchanged); no acme-initiated mutation touched globex data. |
| 6 | Verify both directions | Repeat as a globex user against an acme open record — same isolation holds. (If RLS policies are later added on `attendance_log`, assert the DB session set to acme cannot UPDATE a globex row even via a direct query — currently enforced via EF global query filters + TenantInterceptor, per NFR-4 extension point.) |

## 6. Postconditions
- No cross-tenant clock-out occurred; the target tenant's open record remains untouched; tenant scope is always server-resolved.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
