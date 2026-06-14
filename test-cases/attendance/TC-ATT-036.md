---
id: TC-ATT-036
user_story: US-ATT-003
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-036: Regularization submission requires authentication and the Attendance.Regularize.Self permission (authn + authz)

## 1. Test Objective
Verify the regularization endpoint enforces authentication and authorization: an unauthenticated request is rejected with 401; an authenticated user WITHOUT the `Attendance.Regularize.Self` permission is rejected with 403 (the permission gate is enforced server-side, not merely by hiding the UI button); and an employee can only submit a regularization for THEMSELVES -- a body-injected `employee_id` for another employee is ignored in favor of the authenticated identity. No regularization row is created on any rejected attempt.

## 2. Related Requirements
- User Story: US-ATT-003
- Preconditions: S2 (valid JWT session; `Attendance.Regularize.Self` permission)
- Functional Requirements: FR-2 (employee_id from session, not client input)

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, regularization workflow configured, lookback = 7 days.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Regularize.Self`.
- Employee "Casey Ray" is `active` in acme but does NOT hold `Attendance.Regularize.Self`.
- Employee "Morgan Vale" is another active acme employee (target for the self-scope injection test).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Unauthenticated request | no/invalid bearer token | Expect 401 |
| User without permission | Casey Ray | Expect 403 |
| Self-scope injection | Jordan's token + body employee_id = Morgan Vale | Must be ignored |
| date | today - 2 days | Within lookback |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/attendance/regularizations` with no Authorization header (or an expired/invalid token) | Response 401 Unauthorized; no row created. |
| 2 | As Casey Ray (authenticated, lacks the permission), submit a valid regularization | Response 403 Forbidden; no row created. The block is enforced at the API regardless of any client-side button visibility. |
| 3 | As Jordan Lee, submit with a body injecting `employee_id` = Morgan Vale's id | The server uses the authenticated identity (Jordan Lee); the regularization is created for Jordan only, OR the mismatch is rejected. Morgan Vale gets no regularization. |
| 4 | Verify the database | No row exists from the 401/403 attempts; for step 3, exactly one row scoped to Jordan Lee (employee_id = Jordan), never Morgan. |
| 5 | Confirm authorization audit | The 403 (and optionally the injection attempt) is logged for the tenant per the audit policy. |

## 6. Postconditions
- Unauthenticated and unauthorized submissions are rejected; submissions are always scoped to the authenticated employee.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
