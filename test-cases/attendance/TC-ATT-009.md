---
id: TC-ATT-009
user_story: US-ATT-001
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-009: Clock-in requires authentication and a valid tenant context (authentication)

## 1. Test Objective
Verify that the clock-in endpoint requires a valid authenticated session. Requests with no token, an expired token, or a malformed/invalid token must be rejected with 401 Unauthorized and must not create any `attendance_log` record.

## 2. Related Requirements
- User Story: US-ATT-001
- Preconditions (auth gate): valid JWT with `tenant_id` and `employee_id`
- Non-Functional Requirements: NFR-3
- Dependencies: Authentication module

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled.
- Employee "Jordan Lee" exists and would normally be authorized.

## 4. Test Data
| Sub-case | Authorization | Expected |
|----------|---------------|----------|
| A | No Authorization header | 401 |
| B | Expired JWT | 401 |
| C | Malformed/garbage token | 401 |
| D | Valid JWT but missing `tenant_id` claim | 401/400 (no tenant context) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Sub-case A: `POST /api/v1/attendance/clock-in` with no Authorization header | 401 Unauthorized; no record created. |
| 2 | Sub-case B: send the request with an expired JWT | 401 Unauthorized; no record created. |
| 3 | Sub-case C: send the request with a malformed token | 401 Unauthorized; no record created. |
| 4 | Sub-case D: send a valid-signature JWT lacking the `tenant_id` claim | Request rejected (401/400); the tenant context cannot be established so no tenant-scoped write occurs. |
| 5 | Positive control: valid JWT with tenant and employee claims | 201 Created. |

## 6. Postconditions
- No `attendance_log` records created from any unauthenticated/invalid attempt.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
