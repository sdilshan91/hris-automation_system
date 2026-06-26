---
id: TC-ATT-137
user_story: US-ATT-010
module: Attendance
priority: critical
type: security
status: fail
created: 2026-06-15
---

# TC-ATT-137: Permission scoping -- Manager sees only their team (Attendance.Approve.Team), HR sees all (Attendance.Read.All); scope enforced server-side across dashboard / live-board / reports

## 1. Test Objective
Verify role-based data scoping on the dashboard + reports (BR-3, BR-4): an HR Officer with `Attendance.Read.All` sees the whole tenant, while a Manager scoped by their team permission sees ONLY their direct reports across the dashboard KPIs, live board, department comparison, custom report, and trends -- the scope is enforced server-side (not by hiding UI), and a manager cannot widen scope via parameter tampering.

## 2. Related Requirements
- User Story: US-ATT-010
- Business Rules: BR-3 (board/reports show only employees the caller may view; all for Attendance.Read.All), BR-4 (managers see only their team, scoped by the team permission)
- Functional Requirements: FR-1 (dashboard), FR-2 (live board), FR-3/FR-4/FR-6 (reports/trends) all respect the caller's scope
- Preconditions: §2 (`Attendance.Read.All` + `Reports.View.All` for HR)
- API: GET /dashboard, /dashboard/live-board, /reports/* with scope=

## 3. Preconditions
- Tenant "acme"; HR Officer "Priya" with `Attendance.Read.All` + `Reports.View.All`; Manager "Mark" with the team permission (e.g. `Attendance.Approve.Team` / `Attendance.Read.Team`) over 6 direct reports; the tenant has 20 employees total.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| HR Priya | Attendance.Read.All | all 20 employees |
| Manager Mark | team permission | 6 direct reports |
| out-of-team employee | Asha (not Mark's report) | must be invisible to Mark |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, `GET /dashboard?scope=all` | KPIs computed over all 20 employees (expected/clocked-in/etc. across the tenant). |
| 2 | As Mark, `GET /dashboard?scope=team` | KPIs computed over Mark's 6 direct reports ONLY -- expected/clocked-in/pending reflect the team, not the tenant. |
| 3 | As Mark, `GET /dashboard?scope=all` (attempt to widen) | scope is coerced to team OR 403 -- Mark cannot obtain tenant-wide KPIs (server-enforced, per the US-ATT-008 TC-ATT-112 scope-tamper precedent). **Reported to caller** (403-vs-coerce choice). |
| 4 | As Mark, `GET /dashboard/live-board` | only Mark's 6 reports appear; out-of-team employees (e.g. Asha) are absent. |
| 5 | As Mark, custom report / department comparison / trends | every report dataset is restricted to Mark's team; aggregates (rates, trends) are computed over the team subset, not the tenant. |
| 6 | As Mark, pass `employeeIds=` / `departmentId=` for an out-of-team employee/department | the out-of-team targets resolve to no rows (filtered by team scope); Mark cannot read another team's data via filter injection. |
| 7 | As Priya (HR), the same requests | full tenant scope returned (Attendance.Read.All). |
| 8 | A user with neither permission | 403 on the dashboard/report endpoints (authz fully verified in TC-ATT-140). |

## 6. Postconditions
- Dashboard and report scope is enforced by the caller's permission server-side; managers are confined to their team and cannot widen scope; HR sees the tenant.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Performance test
- [ ] Multi-tenant isolation
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- BR-4 names the manager scope as the team permission; the exact permission string (`Attendance.Read.Team` vs `Attendance.Approve.Team`) is confirmed against the PermissionCatalog (mirrors US-ATT-009 TC-ATT-127 permission-string note). **Reported to caller.**
- The manager-passes-scope=all -> 403-vs-coerced-to-team behaviour is the same ambiguity flagged in US-ATT-008 TC-ATT-112; the test asserts the data is never widened either way. **Reported to caller.**
- This is the in-tenant SCOPE control; cross-TENANT isolation is TC-ATT-ISO-013. Team membership comes from Core HR (ReportsToEmployeeId).
