---
id: TC-ATT-ISO-003
user_story: US-ATT-001
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-ISO-003: Tenant isolation is enforced at the data layer — an employee in Tenant A cannot create an attendance_log in Tenant B

## 1. Test Objective
Verify write-side tenant isolation: an authenticated employee in Tenant A cannot, by any payload manipulation, create or attribute an `attendance_log` to Tenant B. The `tenant_id` must be derived from the resolved server-side tenant context (stamped by the TenantInterceptor), never trusted from the request body, and the EF global query filter / data-layer guard must prevent reading or writing across tenants. (US-ATT-001 NFR-2 names PostgreSQL RLS; this platform currently uses EF Core global query filters + TenantInterceptor. If RLS is added on `attendance_log`, assert the policy blocks a cross-tenant INSERT at the session level here.)

## 2. Related Requirements
- User Story: US-ATT-001
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1 (tenant_id from session context, not client input)

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both exist and are `active`.
- Employee "Jordan Lee" is authenticated in "acme" with `Attendance.Clock.Self`, no open clock-in.
- The UUIDs of a globex tenant and a globex employee are known to the test (to attempt spoofing).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth Context | acme | Jordan Lee |
| Spoofed body tenant_id | globex_tenant_id | Attempt to write into Tenant B |
| Spoofed body employee_id | globex_employee_id | Attempt to clock in another tenant's employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jordan Lee (acme), send `POST /api/v1/attendance/clock-in` with a body that injects `tenant_id = globex_tenant_id` | The injected `tenant_id` is ignored. If a record is created, its `tenant_id` equals acme (from the server context), never globex. |
| 2 | Send a clock-in body injecting `employee_id = globex_employee_id` | The server uses the authenticated employee's id (Jordan Lee); it does not create a record for globex's employee. Request is rejected or scoped to acme. |
| 3 | Verify the database | No `attendance_log` row with `tenant_id = globex_id` was created by these requests. globex's data is untouched. |
| 4 | Verify the TenantInterceptor behavior | The persisted `tenant_id` is stamped from the resolved tenant context on SaveChanges, confirming body values cannot override it. |
| 5 | Confirm via cross-tenant read | Authenticating as globex shows no acme-originated rows and no spoofed rows; isolation holds in both directions. |

## 6. Postconditions
- No cross-tenant `attendance_log` was created; tenant_id always reflects the server-resolved context.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
