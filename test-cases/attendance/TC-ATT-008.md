---
id: TC-ATT-008
user_story: US-ATT-001
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-008: Clock-in is forbidden without the Attendance.Clock.Self permission (authorization)

## 1. Test Objective
Verify that the clock-in endpoint enforces the `Attendance.Clock.Self` permission. An authenticated user who lacks this permission must be denied with 403 Forbidden, and no `attendance_log` record may be created.

## 2. Related Requirements
- User Story: US-ATT-001
- Preconditions (permission gate): `Attendance.Clock.Self`
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled.
- User "Riley Park" is authenticated in "acme" with a valid JWT but does NOT hold the `Attendance.Clock.Self` permission (e.g., a read-only auditor role).
- An employee record exists for the user (or not — both paths are exercised below).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User | Riley Park | Authenticated, lacks Attendance.Clock.Self |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Riley Park, attempt `POST /api/v1/attendance/clock-in` | Response status is 403 Forbidden. The body does not leak internal details. |
| 2 | Verify the UI | If the user reaches a dashboard, the Clock-In action is hidden/disabled for users lacking the permission; direct API invocation still returns 403. |
| 3 | Verify the database | No `attendance_log` record was created. |
| 4 | Grant the permission and retry (positive control) | After `Attendance.Clock.Self` is granted, the same request returns 201 Created. |
| 5 | Verify the permission check is server-side | Hiding the button alone is insufficient; the 403 must come from the API authorization layer, not only client-side guarding. |

## 6. Postconditions
- No record created for the unauthorized attempt.
- Permission grant is the only thing that changes the outcome.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
