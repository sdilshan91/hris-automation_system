---
id: TC-ATT-144
user_story: US-ATT-003
module: Attendance
priority: medium
type: functional
status: automated
created: 2026-07-05
defect:
  - ISSUE-208
automated_by: src/frontend/src/app/layouts/main-layout/main-layout.nav-attendance.spec.ts
---

# TC-ATT-144: Attendance sub-pages are reachable via sidebar nav for the persona that can enter them (ISSUE-208 regression)

## 1. Test Objective
Verify that the sidebar navigation exposes the attendance **sub-pages** (regularization, shifts, overtime, monthly-summary, approvals, late-policy, lateness-score, reconciliation, etc.) — not just the single flat "Attendance" → `/attendance` item — and that each sub-page link is shown only to a persona whose attendance route-guard admits it. Regression guard for **ISSUE-208** (attendance module sub-pages orphaned from in-app navigation).

## 2. Related Requirements
- User Story: US-ATT-003..009 (attendance sub-features: regularization, shift management, overtime, monthly summary, late policy, payroll integration)
- Acceptance Criteria: discoverability/navigation of the US-ATT-003..009 surfaces
- Defect: ISSUE-208

## 3. Preconditions
- `MainLayoutComponent` rendered with the REAL `AuthService` nav filter (`visibleNavItems()`); tenant switcher + idle-timeout HTTP calls stubbed; heavy child components (banner/bell/idle) stubbed.
- A principal established via the real token path (`activateImpersonation`), setting roles + permissions signals.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Privileged persona roles | HR Officer, HR Manager, Tenant Admin | Can enter approver + HR attendance child routes |
| Privileged persona perms | Attendance.View.Own/.Team/.All, .Read.All, .Approve.Team, .Shift.Manage | Passes a permission gate whichever the fix uses |
| Employee persona roles | Employee | Base attendance guard only |
| Employee persona perms | Attendance.View.Own, .Clock.Self, .Regularize.Self | Self views only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Log in as the privileged HR/manager persona; render the layout. | Rendered sidebar contains ≥1 `/attendance/…` sub-route link (pre-fix: only `/attendance`, so 0). |
| 2 | Assert every rendered attendance sub-link is a known attendance child route. | No dead/typo links; all drawn from `attendance.routes.ts`. |
| 3 | Assert ≥1 HR-reachable sub-route (e.g. `/attendance/shifts`, `/attendance/regularization-approvals`) is present. | Privileged persona can navigate to the HR attendance pages. |
| 4 | Log in as the employee persona; render the layout. | Base `/attendance` self view still present; ≥1 employee-reachable sub-route (overtime/lateness-score/regularization) present (pre-fix: none). |
| 5 | Assert the employee is NOT shown an HR-only attendance link (`/attendance/shifts`, `/attendance/monthly-summary`, `/attendance/late-policy`, `/attendance/regularization-approvals`). | No nav link that its route guard would bounce to `/forbidden`. |

## 6. Postconditions
- No state change (pure UI nav-filter assertion).

## 7. Test Category Tags
- [x] Happy path (privileged persona sees its sub-pages)
- [x] Negative test (employee not shown HR-only links)
- [ ] Boundary test
- [x] Security test (persona-scoped nav — no link to a guard-rejected route)
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Automation
- **Harness:** Karma + Jasmine (Angular TestBed). Drives the REAL `AuthService`/`visibleNavItems()` nav filter and queries the rendered `nav.sidebar-nav a.nav-item` hrefs — no reimplementation of the filter.
- **Binding:** `src/frontend/src/app/layouts/main-layout/main-layout.nav-attendance.spec.ts` (`MainLayoutComponent attendance nav visibility (ISSUE-208 / TC-ATT-144)`): `nav_exposes_attendance_subpages_ISSUE208`, `nav_exposes_employee_attendance_subpages_ISSUE208`, and the employee guard-safety invariant.
- **Pre-fix:** the sole attendance navItem is `{ route:'/attendance', permission:'Attendance.View.Own' }` with no children → zero `/attendance/…` sub-route links → the privileged and employee sub-route assertions FAIL. **Post-fix:** the added sub-page nav entries render for the personas their guards admit, so the assertions pass.
- **Note:** the concrete expected sub-routes are asserted via candidate sets (HR-reachable / employee-reachable) keyed on the known `attendance.routes.ts` child paths; if the parallel `main-layout.component.ts` fix exposes a different subset, narrow the candidate lists to the routes it actually added.
