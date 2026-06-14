---
id: TC-ATT-005
user_story: US-ATT-001
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-005: Clock-in is rejected from a non-allowlisted IP when IP allowlist is enforced (negative)

## 1. Test Objective
Verify that when the tenant enables IP-allowlist enforcement (`ip_allowlist_enabled = true`), a clock-in request originating from an IP address that is NOT on the configured allowlist is rejected with the message "Clock-in is only allowed from authorized network locations.", and no `attendance_log` record is created. The complementary allowed-IP case succeeds.

## 2. Related Requirements
- User Story: US-ATT-001
- Acceptance Criteria: AC-5
- Functional Requirements: FR-4, FR-5
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled.
- Tenant attendance settings: `ip_allowlist_enabled = true`, allowlist = [`203.0.113.10`, `203.0.113.0/24`].
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`, no clock-in for the current local day.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| ip_allowlist_enabled | true | Enforced |
| Allowlist | 203.0.113.10, 203.0.113.0/24 | Authorized network |
| Disallowed source IP | 198.51.100.45 | Not on allowlist |
| Allowed source IP | 203.0.113.10 | On allowlist (positive control) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/attendance/clock-in` with the source/forwarded IP set to `198.51.100.45` | Response status is 403 Forbidden (or 422). The clock-in is rejected. |
| 2 | Verify the UI/error message | A non-intrusive inline error reads "Clock-in is only allowed from authorized network locations." with a help link. |
| 3 | Verify the database | No `attendance_log` record was created for the disallowed-IP attempt. The rejected attempt's source IP is captured for audit per FR-5. |
| 4 | Repeat with source IP `203.0.113.10` (allowed) | Response is 201 Created; `attendance_log` is created with `clock_in_ip = 203.0.113.10`. |
| 5 | Verify allowlist evaluation respects CIDR | A source IP within `203.0.113.0/24` (e.g., `203.0.113.77`) is also accepted. |

## 6. Postconditions
- No record created from the disallowed IP; one record created from the allowed IP.
- Source IP recorded for audit on both attempts.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
