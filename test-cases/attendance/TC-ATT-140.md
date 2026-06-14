---
id: TC-ATT-140
user_story: US-ATT-010
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-ATT-140: AuthN / AuthZ -- dashboard / live-board / reports / trends / scheduled-config require auth + Reports.View.All (HR); input sanitised; scheduled-config CRUD restricted + audited

## 1. Test Objective
Verify authentication and authorization across the US-ATT-010 surface (preconditions §2, FR-1/FR-8, NFR-4): every dashboard, live-board, report, trend, export, and scheduled-report-config endpoint requires a valid authenticated session and the appropriate permission (`Attendance.Read.All` / `Reports.View.All` for HR; team permission for managers), rejects unauthenticated (401) and under-privileged (403) callers server-side, sanitises filter inputs, and audits scheduled-config create/update/delete.

## 2. Related Requirements
- User Story: US-ATT-010
- Preconditions: §2 (`Attendance.Read.All` + `Reports.View.All`)
- Functional Requirements: FR-1 (dashboard), FR-8 (scheduled-config CRUD)
- Non-Functional: NFR-4 (tenant isolation -- see TC-ATT-ISO-013)
- API: GET /dashboard, /dashboard/live-board, /reports/department-comparison, /reports/custom (+ /export), /reports/trends, GET/POST/PUT/DELETE /reports/scheduled

## 3. Preconditions
- Tenant "acme"; HR Officer "Priya" (`Attendance.Read.All` + `Reports.View.All`); Manager "Mark" (team permission only); Employee "Asha" (neither); an unauthenticated client.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| HR | Reports.View.All | full access |
| Manager | team permission | team-scoped (TC-ATT-137) |
| Employee | none of the above | 403 expected |
| injection probe | `'; DROP TABLE--`, `<script>` in filter params | sanitisation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Unauthenticated `GET /dashboard` (and every other endpoint) | 401 Unauthorized; no data returned. |
| 2 | As Employee Asha, `GET /dashboard` / `/reports/custom` / `/reports/trends` | 403 Forbidden -- a plain employee has no reporting permission (server-side, not button-hiding). |
| 3 | As Manager Mark | reaches the endpoints but team-scoped only (delegates to TC-ATT-137); cannot widen to all. |
| 4 | As HR Priya | 200 on all read endpoints (full tenant scope). |
| 5 | As Employee Asha, `POST/PUT/DELETE /reports/scheduled` | 403 -- scheduled-report config management is HR-only (`Reports.View.All` / a config-manage permission). |
| 6 | Injection / XSS in filter params (departmentId, employeeIds, report filters, scheduled-config filters jsonb) | parameterised queries / validation -- no SQL execution, payload stored/echoed inertly, no script execution. |
| 7 | Scheduled-config audit (NFR / §7 audit fields) | create/update/delete of a scheduled_report_config is recorded with actor + timestamp, tenant-scoped and immutable. |
| 8 | Permission gate is server-enforced | removing the permission and replaying a previously-allowed request returns 403 (not merely a hidden UI control). |

## 6. Postconditions
- All dashboard/report/scheduled endpoints enforce authn + role-based authz server-side, sanitise inputs, and audit config mutations; no unauthorized data exposure.

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
- The exact permission strings (`Reports.View.All`, the scheduled-config-manage permission, and the manager team permission) are confirmed against the PermissionCatalog -- consistent with US-ATT-009 TC-ATT-127 / US-ATT-007 TC-ATT-098. **Reported to caller.**
- In-tenant SCOPE (manager team vs HR all) is the focus of TC-ATT-137; cross-TENANT isolation is TC-ATT-ISO-013; this TC is the authn/authz + sanitisation + config-audit gate.
