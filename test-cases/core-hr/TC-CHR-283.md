---
id: TC-CHR-283
user_story: US-CHR-011
module: Core HR
priority: high
type: functional
status: blocked
exec_note: "S1: now-testable — re-run against current infra (FE/Docker/Redis/platform-login up)."
created: 2026-06-12
---

# TC-CHR-283: Reporting chain breadcrumb displayed on employee profile

## 1. Test Objective
Verify that an employee's profile displays a reporting chain breadcrumb showing the full path upward from the employee to the top-level manager (Employee -> Manager -> Manager's Manager -> ... -> Top) as a horizontal breadcrumb trail. This validates the UI/UX requirement from Section 8.

## 2. Related Requirements
- User Story: US-CHR-011
- UI/UX Notes: Section 8 (reporting chain breadcrumb)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Chain: Employee E -> Manager M -> VP -> CEO (3 levels above E).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee E | emp@acme.test | reports_to = M |
| Manager M | mgr@acme.test | reports_to = VP |
| VP | vp@acme.test | reports_to = CEO |
| CEO | ceo@acme.test | reports_to = null |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee E's profile page. | The profile loads with a reporting chain breadcrumb visible. |
| 2 | Verify the breadcrumb content. | Breadcrumb reads: "Employee E > Manager M > VP > CEO" (horizontal trail, left-to-right from employee upward). |
| 3 | Verify each name in the breadcrumb is clickable. | Clicking "Manager M" navigates to M's profile. Clicking "VP" navigates to VP's profile. |
| 4 | Navigate to the CEO's profile page. | The breadcrumb shows only "CEO" (no parent above). |

## 6. Postconditions
- No state change; read-only UI verification.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — requires the employee profile/detail view and/or the org-tree hierarchy to verify the reporting-manager field / null-root / unlimited-reports / hierarchy / breadcrumb. The Employee Directory list is crashed (**BUG-099**) so profiles aren't reachable by in-app click, and the org tree renders no nodes (**ISSUE-207**). The "My Team" view (`/employees/my-team`) renders correctly with an empty-state for tenantadmin. Not separately runnable in this FE sweep.

> **Execution 2026-07-01 (triage, acme):** STILL BLOCKED — FE-UI arm (reporting-chain breadcrumb render). Not API-testable this pass. Underlying data gap: no reporting-chain is exposed on the employee detail/profile endpoints and there is no `/reporting-chain` endpoint (see ISSUE-218) — the breadcrumb "Employee > Manager > VP > CEO" has no backing API to render from.
