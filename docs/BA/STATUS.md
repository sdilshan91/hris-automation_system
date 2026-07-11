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

## 1. Authentication (16 stories)
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
> **Enterprise SSO (Microsoft Entra ID) epic — CR-AUTH-001.** PR #112 landed a **working end-to-end POC** (challenge→callback→id_token validation→fail-closed isolation→match/JIT→app JWT + FE). Reconciled status, live test results, and remaining-work checklist: **[SSO-EPIC-STATUS-AND-TODO.md](authentication/SSO-EPIC-STATUS-AND-TODO.md)**. Gated on `PlanFeatureFlags.Sso`; stays feature-flagged off in prod until 012 (DB-backed isolation) lands per BR-5.
- [~] US-AUTH-011 — Entra OIDC authentication foundation *(**POC built, PR #112**: challenge + callback + code-exchange + full id_token validation in `EntraSsoService`; live-verified AC-1/AC-2/AC-5/AC-7. Prod form gated on 012/013-DB)*
- [ ] US-AUTH-012 — Per-tenant SSO configuration *(**REAL GAP** — allow-list still in appsettings; move to DB-backed `TenantAuthSettings`. The genuine next build)*
- [~] US-AUTH-013 — Tenant-scoped tid/domain validation & isolation *(**logic built, PR #112**: `CheckIsolation` + custom per-tid issuer validator, fail-closed; config-driven. DB form delivered by 012; **Must Have** — gates 011 to prod)*
- [~] US-AUTH-014 — User matching, account linking & JIT provisioning *(**built, PR #112**: `AuthService.SsoSignInAsync` — oid→email bootstrap→JIT)*
- [~] US-AUTH-015 — "Sign in with Microsoft" frontend *(**built, PR #112**: button + `sso-callback` route + auth wiring. Per-tenant gating + sso_only UX land with 012/016)*
- [ ] US-AUTH-016 — SSO enforcement, break-glass & admin-consent onboarding *(**REAL GAP** — no enforcement_mode/break-glass/admin-consent yet)*

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
- [~] US-PLT-002 — PostgreSQL Row-Level Security as defense-in-depth tenant isolation *(Phases 1-3 plumbing in PR #51, inert by default. **Phase 4 switch-on = the remaining dev task** — full spec in [`src/backend/HRM.Infrastructure/Persistence/Rls/README.md`](src/backend/HRM.Infrastructure/Persistence/Rls/README.md):*
  1. *Enable-RLS EF migration: `ALTER TABLE … ENABLE/FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation … USING (tenant_id = current_setting('app.current_tenant', true)::uuid)` on every `TenantId`-filtered table (exclude `tenants`/`users`; `roles` = nullable-tenant special case).*
  2. *Route system/admin paths (`DbInitializer`, tenant lookup, system-context, cross-tenant Hangfire) to `ConnectionStrings:PrivilegedConnection` (BYPASSRLS `hrm_owner` from `roles.sql`).*
  3. *Flip `Rls:Enabled=true` + add CI RLS integration tests.*
  - ***Env precondition now MET*** *(native PG18 :5432 + Docker both up — the original "no Docker/Postgres" deferral reason is stale). QA-verified 2026-06-30: live DB has **0 policies / 0 RLS-enabled tables**, flag `false` → genuinely unimplemented (DB availability is NOT the blocker; the migration is). Completing it unblocks the `[DEFERRED]` isolation TCs ADM-ISO-016/020/024/027/031 + ADM-005-21. Run via `/implement-story US-PLT-002` (deliberate dev+review — RLS touches every tenant query).)*
- [x] US-PLT-003 — Serialize API enums as strings + reconcile FE enum casing *(PR #57: global JsonStringEnumConverter + recruitment FE casing; PR #111: leave-management + core-hr FE enum casing reconciled — **COMPLETE**)*
- [ ] US-PLT-004 — Observability & platform NFRs (OTel, health live/ready, per-tenant usage, SLOs) *(**net-new STUB, reconciliation 2026-07-06, Theme I** — no tracing/metrics; US-ADM-002 KPIs hardcoded null. Underpins US-ADM-002 + US-ADM-012.)*
- [ ] US-PLT-005 — Encryption-at-rest for sensitive PII & MFA secrets (pgcrypto/KEK) *(**net-new STUB, reconciliation 2026-07-06, Themes A/D** — plaintext TOTP MFA secret (HIGH) + no column-level PII/tenant-secret encryption. Ref tech-doc §6. RLS half is the existing US-PLT-002.)*

### QA-Surfaced Dev Backlog (from 2026-06-30 isolation + FE testing — fixes/implementations needed to unblock tests)
> These are dev tasks (fixes or unbuilt features) found during the P3 testing campaign. Full detail in [docs/QA/TEST-FINDINGS.md](../QA/TEST-FINDINGS.md). Hand to a fix cycle / `/implement-story`; not auto-picked.
- [ ] **FIX BUG-003 (CRIT, systemic cross-tenant)** — validate the JWT `tenant_id` claim against the subdomain-resolved tenant (root `TenantResolutionMiddleware` / US-AUTH-007). Unblocks/clears the cross-tenant read+write isolation arms across every module.
- [ ] **FIX BUG-107 (HIGH, security)** — impersonation destructive-op blocklist misses `ForcePasswordReset`/`DeactivateUser`/`AssignUserRoles`/`EditUserRoles`; they execute during a full SystemAdmin impersonation. Add them to the hard-block list.
- [ ] **FIX BUG-106 (MED)** — suspended-tenant Tenant Admin/Owner not exempt from the 451 gate → can't reach the read-only suspension landing/export (AC-2 unmet).
- [ ] **FIX BUG-104 + ISSUE-217 (HIGH/MED, one root)** — FE↔BE route mismatch `/tenant/exports` (FE) vs `/tenant/data-exports` (BE); breaks the Data Export UI and the terminating-tenant grace export allowlist.
- [ ] **FIX FE render/contract bugs from the sweep** — BUG-097 (no silent session-restore → reload logs out), BUG-099 (Employee Directory render crash), BUG-100 (Custom Fields render crash), BUG-101/102 (carry-forward NaN / apply-leave empty dropdown), BUG-098 (leave-type null-color null-deref). See TEST-FINDINGS.md BUG-096..104.
- [ ] **BUILD deferred Admin monitoring KPIs** (TC-ADM-002-14..18 `[DEFERRED]`) — aggregate error-rate %, P95 latency, SLA-uptime %, storage/API-call/email usage gauges. Unblocks those TCs.
- [ ] **FIX systemic a11y classes (from P3c-FE deep-a11y, 2026-06-30/07-01)** — these recur on EVERY module's pages, so each is one shared fix:
  - **BUG-096** — `#a3a3a3` (Tailwind `neutral-400`) muted text + green trend-pill fail WCAG AA contrast app-wide → darken the design token(s).
  - **BUG-109** — **every hand-rolled overlay/drawer/modal** (payroll run, attendance regularization, etc.) asserts `aria-modal` but doesn't make the background inert, trap focus, move initial focus, or close on Esc → adopt Angular CDK `Dialog`/`overlay` (focus-trap + `cdkTrapFocus` + inert background) for all overlays.
  - **BUG-108** — focusable `aria-hidden` file inputs nested in `role="button"` drop-zones (upload controls) → `tabindex="-1"` on the hidden input / unnest.
  - **BUG-110** — `role="tablist"` containing non-`tab` children (statutory fiscal-year selector class) → correct ARIA roles.
  - **BUG-111** — dynamic char-counters lack `aria-live`/`role="status"` → add live region.
  - **BUG-112** — `overflow-x-auto` scroll regions lack `tabindex="0"` → make keyboard-scrollable.
- [ ] **BUILD/FIX Core HR functional gaps (P3c-functional, 2026-07-01)** — **BUG-113 HIGH** (employee Create/Edit API has no `LocationId` → employee↔location linking impossible, per-location count always 0, deactivation-guard is dead code — wire `LocationId` into `CreateEmployeeCommand`/`UpdateEmployeeProfileRequest`), **BUG-114 MED** (tenant storage quota `MaxStorageGb` never enforced — no usage sum/gate), **ISSUE-218 MED** (reporting-manager/chain not exposed on `GET /employees/{id}`).
- [ ] **(tracked above) US-PLT-002 RLS** — unblocks the 19 `[DEFERRED]` RLS/at-rest-encryption isolation TCs; env precondition now met.

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
- [ ] US-PRF-011 — Performance calibration workspace *(**net-new STUB, reconciliation 2026-07-06, Theme E** — execution surface for the calibration phase; removes the calibration dead-end trap that permanently locks US-PRF-010.)*

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
- [x] US-ADM-011 — Approval-workflow RUNTIME engine (instances/routing/SLA-escalation/delegation) *(**SHIPPED 2026-07-10** — 011a #238 · 011b parallel+SLA+notifs #239 · 011c delegation + Attendance/Overtime/Offer wiring + read API #240. Wired US-LV-005 AC-4, US-ATT-004 AC-4, US-REC-007 FR-10. FE instance/step-chain viewer deferred → ISSUE-272 (P6).)*
- [ ] US-ADM-012 — Plan/module governance enforcement (runtime gating + usage limits) *(**net-new STUB, reconciliation 2026-07-06, Theme H** — US-ADM-009 config not enforced; disabled-module APIs not 403'd, limits config-only (BUG-114).)*

## 9. Onboarding / Offboarding (6 stories) — COMPLETE ✅
- [x] US-ONB-001 — Create onboarding checklist template *(PR #95)*
- [x] US-ONB-002 — Assign onboarding checklist to new hire *(PR #96)*
- [x] US-ONB-003 — New hire completes onboarding tasks *(PR #97)*
- [x] US-ONB-004 — Asset issuance tracking *(PR #98)*
- [x] US-ONB-005 — Offboarding/exit checklist and clearance *(PR #99)*
- [x] US-ONB-006 — Exit interview recording *(PR #100)*

## 10. Notifications & Audit (5 stories) — COMPLETE ✅
- [x] US-NTF-001 — In-app notification system (SignalR) *(PR #101)*
- [x] US-NTF-002 — Email notification templates per tenant *(PR #102)*
- [x] US-NTF-003 — Notification preferences per user *(PR #103)*
- [x] US-NTF-004 — Audit trail for all data changes *(PR #104)*
- [x] US-NTF-005 — Audit log viewer with filters *(PR #105)*
- [ ] US-NTF-006 — Notification delivery layer (SMTP email + SignalR/in-app dispatch) *(**net-new, reconciliation 2026-07-06, Theme B** — real delivery replacing ~30 `LogOnly*` seams; unblocks the deferred delivery ACs on ~25 done stories. FULL story authored.)*

## 11. Reports & Analytics (5 stories)
- [x] US-RPT-001 — Pre-built HR reports
- [x] US-RPT-002 — Leave and attendance reports
- [x] US-RPT-003 — Payroll reports and summaries
- [x] US-RPT-004 — Export reports to CSV/PDF/Excel
- [x] US-RPT-005 — Dashboard with KPI widgets

## 12. Training & Benefits (epic + 3 core stories) — NET-NEW, backlog
> **Reconciliation 2026-07-06, Theme M** — module had ZERO coverage (no stories/test-cases, never executed). Stubs authored to put it in the backlog; flesh out before build.
- [x] US-TRN-EPIC — Training & Benefits module epic *(v1 SHIPPED 2026-07-10 — 001/002/003 merged; future-split items remain backlog)*
- [x] US-TRN-001 — Training catalog & course enrollment *(SHIPPED 2026-07-10, PR #241 — BE + FE)*
- [x] US-TRN-002 — Benefits plan administration *(SHIPPED 2026-07-10, PR #242 — BE + FE)*
- [x] US-TRN-003 — Benefit eligibility & employee enrollment *(SHIPPED 2026-07-10, PR #243 — BE + FE; manager eligible-plans UI deferred → ISSUE-271 (P6))*

---

## Tally
- Total stories: **105 spine-done** + **10 net-new backlog** (reconciliation 2026-07-06) = **115 tracked**.
- Done spine: **103** — **Authentication (10)**, **Core HR US-CHR-001..012**, **Leave US-LV-001..012**, **Attendance US-ATT-001..010**, **Recruitment US-REC-001..010**, **Payroll US-PAY-001..012** (PR #63–#74), **US-PLT-001** (#50), **Performance US-PRF-001..010** (#75–#84), **Admin Console US-ADM-001..010** (#85–#94), **Onboarding US-ONB-001..006** (#95–#100), **Notifications & Audit US-NTF-001..005** (#101–#105), **Reports & Analytics US-RPT-001..005** (#106–#110).
  - ⚠️ **BUT** ~40 of these `[x]` stories carry **unbuilt ACs** — see the **Deferred-AC Reconciliation** table below. They are not fully done; the spine is.
- In progress: **1** (US-PLT-002 RLS Phase 4 deferred).
- **Net-new backlog (2026-07-06 reconciliation): 10 → 5 shipped 2026-07-10, 5 remain.** SHIPPED: US-ADM-011 (#238-240), US-TRN-EPIC/001/002/003 (#241-243). REMAINING `[ ]`: US-NTF-006 (full, next), US-ADM-012 / US-PRF-011 / US-PLT-004 / US-PLT-005 (stubs).
- **Recommended next build order (updated 2026-07-11):** US-NTF-006 (delivery layer — unblocks the most deferred ACs) → US-PLT-005 (MFA-secret encryption, HIGH) → US-PLT-002 (RLS) → US-PRF-011/US-ADM-012/US-PLT-004.

## Deferred-AC Reconciliation (2026-07-06) — `[x]`-done stories with UNBUILT acceptance criteria

> Source: [docs/QA/COMPLETION-PLAN-2026-07-06.md](../QA/plans/archive/COMPLETION-PLAN-2026-07-06.md) **PART II** (Themes A–M).
> These stories stay `[x]` — their **data-layer spine is built and wired** — but the listed ACs/FRs are
> genuinely unimplemented (almost all *outward delivery* or *cross-module seams* stubbed before the dependency
> existed and never rewired). This is a **status-integrity** annotation, not a re-open. Where a deferred AC is
> unblocked by a net-new story, that story is named. **Do not re-mark these fully done until the noted ACs ship.**

| Story | Deferred AC / FR (unbuilt) | Why / Theme | Unblocked by |
|---|---|---|---|
| US-AUTH-001 | password-reset + lockout email delivery; login **not** rate-limited | LogOnly delivery (B); rate-limit absent (D) | US-NTF-006 |
| US-AUTH-002 | AC-7 JWT key rotation/overlap — single static signing key | no rotation (D) | — |
| US-AUTH-004 | reset-email delivery; password-history configured-but-**unenforced** | B; D | US-NTF-006 |
| US-AUTH-005 | MFA-challenge delivery; challenge **not** rate-limited; MFA secret stored **plaintext** | B; D; A/D | US-NTF-006, US-PLT-005 |
| US-AUTH-007 | FR-9 subdomain cache **not** invalidated on tenant status change (suspended tenant resolves Active for TTL) | D | — |
| US-AUTH-015 | per-tenant SSO gating + `sso_only` UX deferred | lands with US-AUTH-012/016 | US-AUTH-012/016 |
| US-CHR-001 | BUG-113 `LocationId` not wired (employee↔location link impossible); probation-notification delivery | E functional gap; B | US-NTF-006 |
| US-CHR-008 | doc-expiry notification delivery; EXIF not stripped from photos; magic-byte sniff (BUG-058) | B; D | US-NTF-006 |
| US-CHR-009 | status-change / manager-reassignment reminder delivery | B | US-NTF-006 |
| US-CHR-010 | import-completion notification; **custom-field columns in import (FR-11)** — see story AC-K1 | B; K | US-NTF-006 |
| US-CHR-011 | manager-reassignment notification delivery; reporting-manager/chain not on `GET /employees/{id}` (ISSUE-218) | B; E | US-NTF-006 |
| US-CHR-012 | custom-field **cap not enforced**; custom-fields absent from bulk import | H; K | US-ADM-012 |
| US-LV-002 | **FTE proration (BR-2)** + **accrual-frequency scheduling (FR-5)** — see story AC-K1/K2 | K | — |
| US-LV-005 | **AC-4 multi-level routing inert** (`WorkflowInstanceId` null); **BR-4 payroll-lock hardcoded false**; approval-email delivery | C/E; E; B | US-ADM-011, US-NTF-006 |
| US-LV-010 | AC-4 cancellation ignores payroll lock (always "not locked") | E | US-ADM-011 |
| US-LV-011 | **AC-2 auto-LOP inert** — behind `NoOpAttendanceProvider` | E | (attendance provider wiring) |
| US-LV-012 | **FR-1 Dept Leave-Coverage report returns empty** — see story AC-K1 | K | — |
| US-ATT-003 | UTC-only day-boundary/late detection (wrong for non-UTC tenants); request-notification delivery | J (ISSUE-065); B | US-NTF-006 |
| US-ATT-004 | **AC-4 multi-level regularization approval inert**; approval-notification delivery | C; B | US-ADM-011, US-NTF-006 |
| US-ATT-008 | UTC-only late/early detection (ISSUE-065); late-arrival alert delivery | J; B | US-NTF-006 |
| US-ATT-010 | scheduled-report + alert delivery | B | US-NTF-006 |
| US-REC-002 | application-confirmation email; resume magic-byte sniff (BUG-058) | B; D | US-NTF-006 |
| US-REC-004 | stage-change email delivery | B | US-NTF-006 |
| US-REC-005 | interview-schedule notify delivery; **interview-guide attachment (FR-8)** | B; K | US-NTF-006 |
| US-REC-006 | scorecard email delivery; **scorecard versioning** — see story AC-K1 | B; K | US-NTF-006 |
| US-REC-007 | offer email-with-PDF + magic-link delivery; **FR-10 offer-approval routing inert** | B; C/E | US-NTF-006, US-ADM-011 |
| US-REC-008 | status-tracking magic-link email delivery | B | US-NTF-006 |
| US-REC-010 | **AC-3 no user-account creation, AC-2 no salary persistence, AC-4 no "Converted" badge (ISSUE-232)**; welcome-email/onboarding trigger (ISSUE-140) | E | US-NTF-006 |
| US-PAY-009 | **year-end tax-statement PDF (ISSUE-177)** + report PDF export | F | — |
| US-PAY-011 | **entire story purpose unbuilt** — bulk payslip email is LogOnly, nothing delivered | B | US-NTF-006 |
| US-PRF-001 | goals-set notification delivery; **goal-set finalize == 100% (BUG-056)** — see story AC-K1 | B; K | US-NTF-006 |
| US-PRF-002 | self-rating notification delivery; **AC-B1 self-assessment attachment DELETE missing (BUG-243)** | B; F/BUG-243 | US-NTF-006 |
| US-PRF-003 | rating notification delivery | B | US-NTF-006 |
| US-PRF-004 | **AC-B1 cycle rating-scales endpoint missing**; **AC-B2 low-privilege "resolve active cycle" resolver missing — cross-cutting BUG-243 enabler** | F/BUG-243 | — |
| US-PRF-005 | **360 report PDF**; 360 notifications delivery; **AC-B1 reviewer full-replace PUT · AC-B2 standalone tracker · AC-B3 get-form-by-assignment missing (BUG-243)** | F; B; F/BUG-243 | US-NTF-006 |
| US-PRF-006 | **review meeting PDF** | F | — |
| US-PRF-007 | **dashboard PDF export** | F | — |
| US-PRF-008 | **PIP PDF**; PIP notification delivery; **AC-B1 PIP draft/pre-fill endpoint missing (BUG-243)** | F; B; F/BUG-243 | US-NTF-006 |
| US-PRF-010 | **recommendation PDF**; **calibration dead-end trap** (permanent lockout); **AC-B1 completed-cycles picker missing (BUG-243)**; **AC-B2 team-recs = workspace reshape (BUG-243, not a gap)** | F; E; F/BUG-243 | US-PRF-011 |
| US-ADM-002 | monitoring KPIs (error-rate/latency/SLA/usage) **hardcoded null** | I | US-PLT-004 |
| US-ADM-006 | plan-gated enterprise-only settings absent (#17) | H | US-ADM-012 |
| US-ADM-009 | module-gating **not enforced** (disabled-module API not 403'd, no FE guard); usage limits config-only (BUG-114) | H | US-ADM-012 |

### Theme-K follow-up ACs attached to existing stories (see each story's "Follow-up ACs" section)
| Existing story | Attached follow-up | Finding |
|---|---|---|
| US-PAY-001 | AC-K1 SalaryGrade entity | ISSUE-021 |
| US-PRF-001 | AC-K1 goal-set finalize (==100%) | BUG-056 |
| US-LV-002 | AC-K1 FTE proration · AC-K2 accrual-frequency scheduling | LV-002 BR-2/FR-5 |
| US-CHR-010 | AC-K1 custom-field columns in bulk import (spans US-CHR-012) | CHR-010/012 FR-11 |
| US-LV-012 | AC-K1 Dept Leave-Coverage report (empty stub) | LV-012 FR-1 |
| US-REC-006 | AC-K1 scorecard versioning | REC-006 |

### BUG-243 follow-up ACs — Performance FE→BE missing endpoints (attached 2026-07-08)
> Verified per-item against the real controllers (`Feedback360Controller`, `SelfAssessment*Controller`,
> `CyclesController`, `RecommendationController`, `PipController`) — only genuinely-absent routes formalized.
> See each story's "Follow-up ACs (BUG-243 …)" section. AC-B prefix = BUG-243 backlog.
| Existing story | Attached follow-up (AC-B) | Verdict |
|---|---|---|
| US-PRF-002 | AC-B1 self-assessment attachment DELETE | genuinely missing |
| US-PRF-004 | AC-B1 cycle rating-scales endpoint · AC-B2 low-privilege active-cycle **resolver** (cross-cutting enabler) | both missing |
| US-PRF-005 | AC-B1 reviewer full-replace PUT · AC-B2 standalone tracker · AC-B3 get-form-by-assignment | missing (B2 data exists embedded in /results) |
| US-PRF-008 | AC-B1 PIP draft/pre-fill | genuinely missing |
| US-PRF-010 | AC-B1 completed-cycles picker · AC-B2 team-recs | B1 missing; B2 **reshape** (workspace already serves managers) |

---

## Module → directory map
| Module key (CLI arg) | Folder | Story prefix |
|---|---|---|
| `auth` / `authentication` | `docs/BA/authentication/` | US-AUTH |
| `core-hr` | `docs/BA/core-hr/` | US-CHR |
| `leave` / `leave-management` | `docs/BA/leave-management/` | US-LV |
| `attendance` | `docs/BA/attendance/` | US-ATT |
| `recruitment` | `docs/BA/recruitment/` | US-REC |
| `payroll` | `docs/BA/payroll/` | US-PAY |
| `performance` | `docs/BA/performance/` | US-PRF |
| `admin` / `admin-console` | `docs/BA/admin-console/` | US-ADM |
| `onboarding` | `docs/BA/onboarding/` | US-ONB |
| `notifications` | `docs/BA/notifications/` | US-NTF |
| `reports` | `docs/BA/reports/` | US-RPT |
| `training` / `training-benefits` | `docs/BA/training-benefits/` | US-TRN |
| `platform` | `docs/BA/platform/` | US-PLT |
