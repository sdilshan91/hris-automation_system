---
id: TC-ATT-112
user_story: US-ATT-008
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-112: Late/early report -- manager team scope vs HR all scope, with date-range and department filters (AC-5, FR-6)

## 1. Test Objective
Verify AC-5/FR-6: the late/early-departure report lists employees with their late-arrival and early-departure counts for the selected period; a manager sees only their team (scope=team) while HR sees all employees (scope=all); and the report supports date-range, department, and employee filters. Scope is server-resolved from the caller's role/team -- not client-supplied.

## 2. Related Requirements
- User Story: US-ATT-008
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6 (late/early report -- manager team scope, HR all scope; filters: date range, department, employee)
- API: GET /api/v1/attendance/late-early/report?from=&to=&scope=team|all

## 3. Preconditions
- Tenant "acme"; manager "Mark" with 4 direct reports (in dept Engineering); HR Officer "Priya" with `Attendance.Read.All`; employees across Engineering + Sales with seeded late/early records for the period.
- A separate tenant employee exists to confirm cross-tenant exclusion (see TC-ATT-ISO-011).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| from / to | 2026-05-01 / 2026-05-31 | selected period |
| scope (manager) | team | Mark's direct reports only |
| scope (HR) | all | all tenant employees |
| department filter | Engineering | scopes HR report |
| seeded counts | per employee: late_count, early_departure_count | report columns |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Mark, `GET /late-early/report?from=2026-05-01&to=2026-05-31&scope=team` | 200; rows for Mark's 4 direct reports ONLY, each with late_count + early_departure_count for the period (AC-5). No non-team employees appear. |
| 2 | As Mark, request `scope=all` | The request is restricted to his team regardless of the requested scope (a manager cannot widen to all) -- either 403 or silently team-scoped; record the contract. |
| 3 | As Priya (HR), `GET /late-early/report?from=...&to=...&scope=all` | 200; rows for ALL acme employees with their late/early counts (FR-6 all scope). |
| 4 | As Priya, add `&departmentId=Engineering` | The report is scoped to Engineering employees only (FR-6 department filter). |
| 5 | As Priya, add `&employeeId=` for a single employee | The report returns that one employee's late/early counts for the period (FR-6 employee filter). |
| 6 | Vary the date range (e.g. a single week) | Counts recompute for the narrower window -- only lates/earlies within from..to are counted. |
| 7 | Verify conditional formatting input | Rows whose late_count exceeds the chronic threshold are marked for amber highlighting (§8) -- the data flag is present; the visual rendering is asserted in TC-ATT-116. |

## 6. Postconditions
- No state change; the report reflects role-scoped, period- and filter-bounded late/early counts, tenant-scoped to acme.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The manager/HR scope MUST be enforced server-side from the caller's identity/team, not trusted from the `scope` query param (Step 2). **Reported to caller** -- confirm whether a manager passing scope=all is rejected (403) or coerced to team; either is acceptable provided the data is never widened.
- Tenant isolation of the report is covered by TC-ATT-ISO-011; authn/authz by TC-ATT-117.
