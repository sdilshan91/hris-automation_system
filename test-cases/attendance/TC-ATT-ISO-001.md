---
id: TC-ATT-ISO-001
user_story: US-ATT-001
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-ISO-001: Employee in Tenant A cannot see or retrieve Tenant B's attendance records

## 1. Test Objective
Verify that attendance data is fully tenant-isolated on reads: a user authenticated in Tenant A cannot list or retrieve any `attendance_log` record belonging to Tenant B. This exercises EF Core global query filters (the codebase's tenant-isolation mechanism) and ensures no cross-tenant leakage via the API. (Note: US-ATT-001 NFR-2 specifies PostgreSQL RLS; this platform currently enforces isolation via EF Core global query filters + TenantInterceptor — if RLS policies are later added on `attendance_log`, extend Step 5 to assert them at the DB session level.)

## 2. Related Requirements
- User Story: US-ATT-001
- Non-Functional Requirements: NFR-2
- Assumptions/Constraints: S10 (RLS prevents cross-tenant access)

## 3. Preconditions
- Tenant "acme" exists with attendance records for employee "Jordan Lee".
- Tenant "globex" exists with attendance records for employee "Sam Doe".
- A user with `Attendance.Clock.Self` (and any read permission) is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has Jordan Lee's attendance_log rows |
| Tenant B | globex | Has Sam Doe's attendance_log rows |
| Auth Context | acme | User authenticated in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in "acme"; JWT carries acme's `tenant_id` | Tenant context resolves to acme. |
| 2 | Send `GET /api/v1/attendance` (or the today-status endpoint) | Response contains only acme attendance records. Zero globex records. |
| 3 | Attempt `GET /api/v1/attendance/{globex_attendance_log_id}` using the UUID of one of Sam Doe's records | Response is 404 Not Found (EF global query filter excludes it); never 200 with another tenant's data. |
| 4 | Switch to "globex" context and repeat | globex sees only Sam Doe's records; no acme records. |
| 5 | Verify at the database level | `SELECT * FROM attendance_log WHERE tenant_id = acme_id` returns only acme rows; `... = globex_id` returns only globex rows. (If RLS policies exist, confirm a session set to acme cannot read globex rows even with a direct query.) |

## 6. Postconditions
- No cross-tenant attendance data was exposed via API or query.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
