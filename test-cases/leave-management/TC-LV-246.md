---
id: TC-LV-246
user_story: US-LV-012
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-246: Role-based access — a manager sees only their team's data in reports (BR-2, Test Hint)

## 1. Test Objective
Verify BR-2 (manager branch): when a manager runs a report, the data is scoped to their direct reports (and sub-tree per the reporting structure), and they cannot see employees outside their team — even by tampering with filter parameters.

## 2. Related Requirements
- User Story: US-LV-012
- Business Rules: BR-2
- Cross-ref: US-LV-004/US-LV-009 (manager scope = ReportsToEmployeeId)

## 3. Preconditions
- Tenant "acme"; manager "Aisha" with direct reports; other employees report to a different manager.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager | Aisha | team scope |
| Team members | Aisha's direct reports | expected visible |
| Out-of-team | another manager's reports | must be excluded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Aisha, run a report a manager is permitted to view | Only Aisha's team members appear; out-of-team employees are absent. |
| 2 | Tamper: pass a `departmentId`/`employeeId` for an out-of-team employee | The server ignores/forbids the override and still returns only Aisha's team (no scope escalation); cross-ref the US-LV-009 parameter-tampering defense. |
| 3 | Confirm aggregates are team-scoped | Utilization/absenteeism totals are computed over Aisha's team only, not the whole tenant. |
| 4 | Confirm a manager cannot access HR-only reports | If a report is HR-only, the manager is denied (403) rather than served a scoped variant (cross-ref TC-LV-250). |

## 6. Postconditions
- Managers see only their team's report data; parameter tampering cannot widen the scope.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
