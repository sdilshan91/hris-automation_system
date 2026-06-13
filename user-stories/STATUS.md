# Implementation Status Tracker

> Single source of truth used by `/implement-all` to find the next story to build.
> Updated by the skill after each story PR is opened. Safe to hand-edit if you complete or skip work manually.
>
> **Statuses:**
> - `[ ]` pending — not started
> - `[~]` in-progress — branch open, PR not yet merged
> - `[x]` done — PR merged into `main`
> - `[s]` skipped — won't be implemented (note why)
>
> **Loop convention:** `/implement-all` picks the **first `[ ]`** story (scoped by module if you pass an arg, else by priority order below). It opens **one branch + one PR per story** containing FE + BE + QA changes. After you merge the PR, run `/implement-all` again to pick up the next story.

## Module Priority Order
1. authentication
2. core-hr
3. leave-management
4. attendance
5. recruitment
6. payroll
7. performance
8. admin-console
9. onboarding
10. notifications
11. reports

---

## 1. Authentication (10 stories)
- [x] US-AUTH-001 — Admin login with username and password *(scaffold impl + 28 TCs exist; verify)*
- [x] US-AUTH-002 — JWT token issuance and refresh token flow *(scaffold impl exists)*
- [x] US-AUTH-003 — User logout and token invalidation *(scaffold impl exists)*
- [x] US-AUTH-004 — Password reset flow *(scaffold impl exists)*
- [~] US-AUTH-005 — Multi-factor authentication (TOTP) *(implemented; PR #1 open)*
- [x] US-AUTH-006 — Role-based access control (RBAC) per tenant *(PR #2 open)*
- [x] US-AUTH-007 — Tenant resolution from subdomain *(PR #5 open)*
- [x] US-AUTH-008 — Cross-tenant user switching *(merged, PR #6)*
- [x] US-AUTH-009 — Session management and concurrent session limits *(PR #7)*
- [x] US-AUTH-010 — Account lockout after failed attempts *(PR #8)*

## 2. Core HR (12 stories)
- [x] US-CHR-001 — Add new employee with personal information *(PR #11)*
- [x] US-CHR-002 — View and edit employee profile *(PR #13)*
- [x] US-CHR-003 — Employee directory with search and filters *(PR #14)*
- [x] US-CHR-004 — Create and manage departments *(PR #9; built ahead of CHR-001 per dependency order)*
- [x] US-CHR-005 — Create and manage job titles and positions *(PR #10)*
- [x] US-CHR-006 — Organization tree/hierarchy visualization *(PR #16)*
- [x] US-CHR-007 — Manage office locations *(PR #17)*
- [x] US-CHR-008 — Employee document management *(PR #18)*
- [x] US-CHR-009 — Employee status management *(PR #19)*
- [x] US-CHR-010 — Bulk employee import via CSV/Excel *(PR #20)*
- [x] US-CHR-011 — Employee reporting structure *(PR #21)*
- [x] US-CHR-012 — Custom fields per tenant *(PR #22)*

## 3. Leave Management (12 stories)
- [x] US-LV-001 — Configure leave types per tenant *(PR #23)*
- [x] US-LV-002 — Set yearly leave entitlements *(PR #24)*
- [ ] US-LV-003 — Employee applies for leave
- [ ] US-LV-004 — Manager views pending leave queue
- [ ] US-LV-005 — Manager approves or rejects leave
- [ ] US-LV-006 — Leave balance dashboard
- [ ] US-LV-007 — Holiday calendar management
- [ ] US-LV-008 — Leave carry-forward and expiry rules
- [ ] US-LV-009 — Team leave calendar view
- [ ] US-LV-010 — Leave cancellation by employee
- [ ] US-LV-011 — Compulsory leave / LOP handling
- [ ] US-LV-012 — Leave reports and analytics

## 4. Attendance (10 stories)
- [ ] US-ATT-001 — Employee clock-in with optional geolocation
- [ ] US-ATT-002 — Employee clock-out with hours auto-calculation
- [ ] US-ATT-003 — Attendance regularization request
- [ ] US-ATT-004 — Manager approves/rejects regularization
- [ ] US-ATT-005 — Shift management and assignment
- [ ] US-ATT-006 — Overtime tracking and approval
- [ ] US-ATT-007 — Monthly attendance summary
- [ ] US-ATT-008 — Late arrival and early departure tracking
- [ ] US-ATT-009 — Attendance integration with payroll
- [ ] US-ATT-010 — Attendance dashboard and reports

## 5. Recruitment (10 stories)
- [ ] US-REC-001 — Create and publish job vacancy
- [ ] US-REC-002 — Applicant submits application with resume
- [ ] US-REC-003 — Recruiter views applicant pipeline
- [ ] US-REC-004 — Move applicant through pipeline stages
- [ ] US-REC-005 — Schedule interviews and notify participants
- [ ] US-REC-006 — Interviewer submits scorecard
- [ ] US-REC-007 — Generate and send offer letter
- [ ] US-REC-008 — Applicant tracks application status
- [ ] US-REC-009 — Recruitment dashboard and analytics
- [ ] US-REC-010 — Convert applicant to employee record

## 6. Payroll (12 stories)
- [ ] US-PAY-001 — Configure salary structure and components
- [ ] US-PAY-002 — Assign salary structure to employee
- [ ] US-PAY-003 — Run monthly payroll
- [ ] US-PAY-004 — Generate individual payslips
- [ ] US-PAY-005 — Employee views and downloads payslips
- [ ] US-PAY-006 — Statutory deductions configuration
- [ ] US-PAY-007 — Payroll adjustments (bonus, deductions)
- [ ] US-PAY-008 — Payroll approval workflow
- [ ] US-PAY-009 — Payroll reports and analytics
- [ ] US-PAY-010 — Attendance/leave integration into payroll
- [ ] US-PAY-011 — Bulk payslip email distribution
- [ ] US-PAY-012 — Payroll history and audit trail

## 7. Performance Management (10 stories)
- [ ] US-PRF-001 — Manager sets goals/KPIs for team
- [ ] US-PRF-002 — Employee self-rates against goals
- [ ] US-PRF-003 — Manager rates employee performance
- [ ] US-PRF-004 — HR creates appraisal cycles
- [ ] US-PRF-005 — 360-degree review
- [ ] US-PRF-006 — Review meeting notes and sign-off
- [ ] US-PRF-007 — Performance dashboard and analytics
- [ ] US-PRF-008 — Performance improvement plan (PIP)
- [ ] US-PRF-009 — Goal tracking with progress updates
- [ ] US-PRF-010 — Performance-based recommendations

## 8. Admin Console (10 stories)
- [ ] US-ADM-001 — System Admin provisions new tenant
- [ ] US-ADM-002 — Monitor platform health and usage
- [ ] US-ADM-003 — Impersonate tenant user with audit
- [ ] US-ADM-004 — Suspend/terminate a tenant
- [ ] US-ADM-005 — Manage users and role assignments
- [ ] US-ADM-006 — Configure company settings
- [ ] US-ADM-007 — Manage approval workflows
- [ ] US-ADM-008 — View audit logs
- [ ] US-ADM-009 — Manage subscription plans
- [ ] US-ADM-010 — Tenant data export on demand

## 9. Onboarding / Offboarding (6 stories)
- [ ] US-ONB-001 — Create onboarding checklist template
- [ ] US-ONB-002 — Assign onboarding checklist to new hire
- [ ] US-ONB-003 — New hire completes onboarding tasks
- [ ] US-ONB-004 — Asset issuance tracking
- [ ] US-ONB-005 — Offboarding/exit checklist and clearance
- [ ] US-ONB-006 — Exit interview recording

## 10. Notifications & Audit (5 stories)
- [ ] US-NTF-001 — In-app notification system (SignalR)
- [ ] US-NTF-002 — Email notification templates per tenant
- [ ] US-NTF-003 — Notification preferences per user
- [ ] US-NTF-004 — Audit trail for all data changes
- [ ] US-NTF-005 — Audit log viewer with filters

## 11. Reports & Analytics (5 stories)
- [ ] US-RPT-001 — Pre-built HR reports
- [ ] US-RPT-002 — Leave and attendance reports
- [ ] US-RPT-003 — Payroll reports and summaries
- [ ] US-RPT-004 — Export reports to CSV/PDF/Excel
- [ ] US-RPT-005 — Dashboard with KPI widgets

---

## Tally
- Total stories: **102**
- Done: **17** (AUTH-001..004 scaffolded; AUTH-006 PR #2; AUTH-007 PR #5; **Core HR US-CHR-001..012 COMPLETE** — #13,#14,#16,#17,#18,#19,#20,#21,#22; Leave: LV-001 #23, LV-002 #24)
- In progress: **1** (AUTH-005 in PR #1)
- Pending: **83**

## Module → directory map
| Module key (CLI arg) | Folder | Story prefix |
|---|---|---|
| `auth` / `authentication` | `user-stories/authentication/` | US-AUTH |
| `core-hr` | `user-stories/core-hr/` | US-CHR |
| `leave` / `leave-management` | `user-stories/leave-management/` | US-LV |
| `attendance` | `user-stories/attendance/` | US-ATT |
| `recruitment` | `user-stories/recruitment/` | US-REC |
| `payroll` | `user-stories/payroll/` | US-PAY |
| `performance` | `user-stories/performance/` | US-PRF |
| `admin` / `admin-console` | `user-stories/admin-console/` | US-ADM |
| `onboarding` | `user-stories/onboarding/` | US-ONB |
| `notifications` | `user-stories/notifications/` | US-NTF |
| `reports` | `user-stories/reports/` | US-RPT |
