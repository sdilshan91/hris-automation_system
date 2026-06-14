---
id: TC-ATT-063
user_story: US-ATT-005
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-063: Shift management requires authentication and the Attendance.Shift.Manage permission (HR-only); employees/managers are denied

## 1. Test Objective
Verify authentication and authorization on every shift endpoint (Preconditions S2: HR holds the manage permission): all shift CRUD + assign + clone endpoints require a valid authenticated session and the `Attendance.Shift.Manage` permission. Unauthenticated calls get 401; authenticated callers WITHOUT the permission (regular Employee, line Manager) get 403 -- enforced server-side, not merely by hiding the UI. The read-only shift-resolve endpoint follows its documented authorization (employee may resolve own shift).

## 2. Related Requirements
- User Story: US-ATT-005
- Preconditions: Section 2 (HR Officer with `Attendance.*.All` / `Attendance.Shift.Manage`)
- Endpoints: GET/POST shifts; PUT/DELETE shifts/{id}; POST .../clone; POST .../assign; GET employees/{id}/shift

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled.
- Users: HR Officer "Priya" (has `Attendance.Shift.Manage`); Manager "Dana" and Employee "Jordan" (do NOT have it).
- A shift "Day Shift" (shift_id known) exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Permission gate | Attendance.Shift.Manage | HR-only |
| No-token call | (missing/invalid JWT) | -> 401 |
| Non-HR caller | Manager / Employee | -> 403 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `POST/PUT/DELETE .../shifts`, `.../clone`, `.../assign` with NO / invalid token | Each returns 401; no state change. |
| 2 | As Manager Dana (no manage permission), `POST .../shifts` (create) | Response 403; shift not created (server-side authz, not UI-only). |
| 3 | As Employee Jordan, `PUT .../shifts/{id}` and `POST .../shifts/{id}/assign` | Response 403 for each; no mutation. |
| 4 | As Employee Jordan, `DELETE .../shifts/{id}` | Response 403; shift intact. |
| 5 | As HR Priya, the same create/update/assign/clone/delete calls | Authorized (201/200/204 as applicable) -- positive control proving the gate admits the right role. |
| 6 | Resolve endpoint authorization | `GET .../employees/{self}/shift` as Jordan is permitted for his own id per the documented rule; `GET .../employees/{otherEmployee}/shift` without manage/team scope is denied (403) or self-scoped. |

## 6. Postconditions
- Only authenticated HR with `Attendance.Shift.Manage` can manage/assign shifts; unauthenticated -> 401, unauthorized roles -> 403; resolve is self/scope-limited.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
