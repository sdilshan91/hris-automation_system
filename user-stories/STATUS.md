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
- [x] US-LV-003 — Employee applies for leave *(PR #29)*
- [x] US-LV-004 — Manager views pending leave queue *(PR #30)*
- [x] US-LV-005 — Manager approves or rejects leave *(PR #31)*
- [x] US-LV-006 — Leave balance dashboard *(PR #32)*
- [x] US-LV-007 — Holiday calendar management *(PR #33)*
- [x] US-LV-008 — Leave carry-forward and expiry rules *(PR #34)*
- [x] US-LV-009 — Team leave calendar view *(PR #35)*
- [x] US-LV-010 — Leave cancellation by employee *(PR #36)*
- [x] US-LV-011 — Compulsory leave / LOP handling *(PR #37)*
- [x] US-LV-012 — Leave reports and analytics *(PR #38)*

## 4. Attendance (10 stories) — COMPLETE ✅
- [x] US-ATT-001 — Employee clock-in with optional geolocation *(PR #39)*
- [x] US-ATT-002 — Employee clock-out with hours auto-calculation *(PR #40)*
- [x] US-ATT-003 — Attendance regularization request *(PR #41)*
- [x] US-ATT-004 — Manager approves/rejects regularization *(PR #42)*
- [x] US-ATT-005 — Shift management and assignment *(PR #43)*
- [x] US-ATT-006 — Overtime tracking and approval *(PR #44)*
- [x] US-ATT-007 — Monthly attendance summary *(PR #45)*
- [x] US-ATT-008 — Late arrival and early departure tracking *(PR #46)*
- [x] US-ATT-009 — Attendance integration with payroll *(PR #47)*
- [x] US-ATT-010 — Attendance dashboard and reports *(PR #48)*

## 5. Recruitment (10 stories) — COMPLETE ✅
- [x] US-REC-001 — Create and publish job vacancy *(PR #49)*
- [x] US-REC-002 — Applicant submits application with resume *(PR #53)*
- [x] US-REC-003 — Recruiter views applicant pipeline *(PR #54)*
- [x] US-REC-004 — Move applicant through pipeline stages *(PR #55)*
- [x] US-REC-005 — Schedule interviews and notify participants *(PR #56)*
- [x] US-REC-006 — Interviewer submits scorecard *(PR #58)*
- [x] US-REC-007 — Generate and send offer letter *(PR #59)*
- [x] US-REC-008 — Applicant tracks application status *(PR #60)*
- [x] US-REC-009 — Recruitment dashboard and analytics *(PR #61)*
- [x] US-REC-010 — Convert applicant to employee record *(PR #62)*

## Platform / Cross-Cutting Tech Debt (3 stories)
> Cross-cutting fixes surfaced during the feature loop. Not part of a feature module; schedule deliberately. NOT auto-picked by `/implement-all` unless scoped with the `platform` arg.
- [x] US-PLT-001 — Global API response envelope unwrapping (frontend interceptor) *(PR #50; surfaced in US-REC-001 / PR #49)*
- [~] US-PLT-002 — PostgreSQL Row-Level Security as defense-in-depth tenant isolation *(Phases 1-3 plumbing in PR #51, inert by default; **Phase 4 switch-on DEFERRED** — needs Docker/Postgres env, see Persistence/Rls/README.md)*
- [~] US-PLT-003 — Serialize API enums as strings + reconcile FE enum casing *(PR #57: global JsonStringEnumConverter DONE + recruitment FE casing DONE; **residual**: leave-management + core-hr FE enum unions still lowercase/kebab — no regression, deferred follow-up)*

## 6. Payroll (12 stories) — COMPLETE ✅
- [x] US-PAY-001 — Configure salary structure and components *(PR #63)*
- [x] US-PAY-002 — Assign salary structure to employee *(PR #64)*
- [x] US-PAY-003 — Run monthly payroll *(PR #65)*
- [x] US-PAY-004 — Generate individual payslips *(PR #66)*
- [x] US-PAY-005 — Employee views and downloads payslips *(PR #67)*
- [x] US-PAY-006 — Statutory deductions configuration *(PR #68)*
- [x] US-PAY-007 — Payroll adjustments (bonus, deductions) *(PR #69)*
- [x] US-PAY-008 — Payroll approval workflow *(PR #70)*
- [x] US-PAY-009 — Payroll reports and analytics *(PR #71)*
- [x] US-PAY-010 — Attendance/leave integration into payroll *(PR #72)*
- [x] US-PAY-011 — Bulk payslip email distribution *(PR #73)*
- [x] US-PAY-012 — Payroll history and audit trail *(PR #74)*

## 7. Performance Management (10 stories) — COMPLETE ✅
- [x] US-PRF-001 — Manager sets goals/KPIs for team *(PR #75)*
- [x] US-PRF-002 — Employee self-rates against goals *(PR #76)*
- [x] US-PRF-003 — Manager rates employee performance *(PR #77)*
- [x] US-PRF-004 — HR creates appraisal cycles *(PR #78)*
- [x] US-PRF-005 — 360-degree review *(PR #79)*
- [x] US-PRF-006 — Review meeting notes and sign-off *(PR #80)*
- [x] US-PRF-007 — Performance dashboard and analytics *(PR #81)*
- [x] US-PRF-008 — Performance improvement plan (PIP) *(PR #82)*
- [x] US-PRF-009 — Goal tracking with progress updates *(PR #83)*
- [x] US-PRF-010 — Performance-based recommendations *(PR #84)*

## 8. Admin Console (10 stories) — COMPLETE ✅
- [x] US-ADM-001 — System Admin provisions new tenant *(PR #85)*
- [x] US-ADM-002 — Monitor platform health and usage *(PR #86)*
- [x] US-ADM-003 — Impersonate tenant user with audit *(PR #87)*
- [x] US-ADM-004 — Suspend/terminate a tenant *(PR #88)*
- [x] US-ADM-005 — Manage users and role assignments *(PR #89)*
- [x] US-ADM-006 — Configure company settings *(PR #90)*
- [x] US-ADM-007 — Manage approval workflows *(PR #91)*
- [x] US-ADM-008 — View audit logs *(PR #92)*
- [x] US-ADM-009 — Manage subscription plans *(PR #93)*
- [x] US-ADM-010 — Tenant data export on demand *(PR #94)*

## 9. Onboarding / Offboarding (6 stories) — COMPLETE ✅
- [x] US-ONB-001 — Create onboarding checklist template *(PR #95)*
- [x] US-ONB-002 — Assign onboarding checklist to new hire *(PR #96)*
- [x] US-ONB-003 — New hire completes onboarding tasks *(PR #97)*
- [x] US-ONB-004 — Asset issuance tracking *(PR #98)*
- [x] US-ONB-005 — Offboarding/exit checklist and clearance *(PR #99)*
- [x] US-ONB-006 — Exit interview recording *(PR #100)*

## 10. Notifications & Audit (5 stories)
- [~] US-NTF-001 — In-app notification system (SignalR)
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
- Total stories: **105** (incl. 3 Platform/tech-debt)
- Done: **93** — **Authentication (10)**, **Core HR US-CHR-001..012**, **Leave US-LV-001..012**, **Attendance US-ATT-001..010**, **Recruitment US-REC-001..010**, **Payroll US-PAY-001..012 COMPLETE** (PR #63–#74), **US-PLT-001** (#50), **Performance US-PRF-001..010 COMPLETE** (#75–#84), **Admin Console US-ADM-001..010 COMPLETE** (#85–#94), **Onboarding US-ONB-001..006 COMPLETE** (#95–#100)
- In progress: **2** (US-PLT-002 RLS Phase 4 deferred; US-PLT-003 FE enum-casing residual)
- Pending: **10** — Notifications (5), Reports (5)
- **Next module by priority: Notifications & Audit (US-NTF-001..005)**

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
| `platform` | `user-stories/platform/` | US-PLT |
