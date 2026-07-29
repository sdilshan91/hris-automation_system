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
- [x] US-AUTH-005 — Multi-factor authentication (TOTP) *(shipped; `ITotpService` + enroll/verify/challenge live. **Deferred AC:** challenge not rate-limited. ⚠ The long-repeated "MFA secret stored plaintext" claim is **FALSE** — corrected 2026-07-29: it is Data-Protection-wrapped with a Postgres-persisted key ring (PR #224 / ISSUE-247), and legacy rows are now auto-healed on startup by US-PLT-005 Scope A.)*
- [x] US-AUTH-006 — Role-based access control (RBAC) per tenant *(PR #2 open)*
- [x] US-AUTH-007 — Tenant resolution from subdomain *(PR #5 open)*
- [x] US-AUTH-008 — Cross-tenant user switching *(merged, PR #6)*
- [x] US-AUTH-009 — Session management and concurrent session limits *(PR #7)*
- [x] US-AUTH-010 — Account lockout after failed attempts *(PR #8)*
> **Enterprise SSO (Microsoft Entra ID) epic — CR-AUTH-001.** PR #112 landed a **working end-to-end POC** (challenge→callback→id_token validation→fail-closed isolation→match/JIT→app JWT + FE). Reconciled status, live test results, and remaining-work checklist: **[SSO-EPIC-STATUS-AND-TODO.md](authentication/SSO-EPIC-STATUS-AND-TODO.md)**. Gated on `PlanFeatureFlags.Sso`. **EPIC COMPLETE (2026-07-28)** — 011/013/014/015 (#112) + 012 (#444) + 016 (#446); the BR-5 prod gate is satisfied (DB-backed per-tenant isolation shipped). Remaining SSO work is **test-ledger only**: the 5 `[b]` TCs in `TEST-STATUS.md` were blocked on "not implemented" and need a re-run.
- [x] US-AUTH-011 — Entra OIDC authentication foundation *(**PR #112** challenge + callback + code-exchange + full id_token validation in `EntraSsoService`; live-verified AC-1/AC-2/AC-5/AC-7. Prod form completed by 012/013-DB. Structured SSO failure audit events → ISSUE-328 RESOLVED (PR #450); dead `User.Read` scope dropped (ISSUE-329). TCs authored under ISSUE-325.)*
- [x] US-AUTH-012 — Per-tenant SSO configuration *(**SHIPPED — PR #444**: `Tenant.sso_enabled`/`allowed_entra_tenant_ids`/`allowed_email_domains`/`jit_enabled`/`jit_default_role`/`enforcement_mode` + EF migration `AddTenantSsoSettings`; `403 sso_not_entitled` entitlement gate; validators; `sso_config_updated` audit + per-tenant `SsoSettingsSnapshot` cache; FE tenant-admin SSO page + 27 Karma specs; 12 IEEE-829 TCs)*
- [x] US-AUTH-013 — Tenant-scoped tid/domain validation & isolation *(**PR #112** `CheckIsolation` + custom per-tid issuer validator, fail-closed; **DB-backed form delivered by 012 (#444)** — allow-list now reads `TenantAuthSettings`, not appsettings)*
- [x] US-AUTH-014 — User matching, account linking & JIT provisioning *(**PR #112**: `AuthService.SsoSignInAsync` — oid→email bootstrap→JIT; JIT now gated by the per-tenant `jit_enabled`/`jit_default_role` from 012)*
- [x] US-AUTH-015 — "Sign in with Microsoft" frontend *(**PR #112** button + `sso-callback` route + auth wiring; **per-tenant gating + `sso_only` UX delivered with 012 (#444) / 016 (#446)**)*
- [x] US-AUTH-016 — SSO enforcement, break-glass & admin-consent onboarding *(**SHIPPED — PR #446**: `enforcement_mode` login enforcement (`SsoLoginEnforcementPostgresTests`), `BreakGlassLoginCommand` + `IBreakGlassNotificationService`, admin-consent onboarding URL. Break-glass MFA-path audit gap → ISSUE-327 RESOLVED (PR #447). Redirect model recorded in the 2026-07-28 SSO multi-tenant ADR.)*

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
- [x] US-CHR-013 — Employee FTE & work arrangement *(**SHIPPED — CAL-6, PR #316**: `Employee.Fte` + proration (closes US-LV-002 AC-K1) · `FteScaledOvertimeBase` (default off) · `Employee.WorkArrangement` + Remote geofence exemption + FE employee-form. TCs TC-CHR-326/327/328/329 + TC-ATT-152 authored; **not yet executed** — see TEST-STATUS.)*

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
- [x] US-ATT-011 — Location-aware working calendar & location-scoped attendance policy *(**SHIPPED — CAL-1/4a/4b/5, PRs #310/#313/#314/#315**: AC-1+AC-2 `Location.DefaultShiftId` + four-tier `ShiftScheduleResolver` (Employee→Location→Tenant→Mon–Fri) · AC-3 `AttendanceSettings.LocationId` + `AttendancePolicyResolver` + tenant/per-location CRUD · AC-4/FR-5 effective-dated holiday-exclusion payroll denominator. TCs TC-ATT-145..156 + ISO-014/015/016 authored; **not yet executed** — see TEST-STATUS. Residual: BUG-287 (default-shift delete guard).)*

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

## Platform / Cross-Cutting Tech Debt (6 stories)
> Cross-cutting fixes surfaced during the feature loop. Not part of a feature module; schedule deliberately. NOT auto-picked by `/implement-all` unless scoped with the `platform` arg.
- [x] US-PLT-001 — Global API response envelope unwrapping (frontend interceptor) *(PR #50; surfaced in US-REC-001 / PR #49)*
- [~] US-PLT-002 — PostgreSQL Row-Level Security as defense-in-depth tenant isolation *(Phases 1-3 plumbing in PR #51, inert by default. **Phase 4 switch-on = the remaining dev task** — full spec in [`src/backend/HRM.Infrastructure/Persistence/Rls/README.md`](src/backend/HRM.Infrastructure/Persistence/Rls/README.md):*
  1. *Enable-RLS EF migration: `ALTER TABLE … ENABLE/FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation … USING (tenant_id = current_setting('app.current_tenant', true)::uuid)` on every `TenantId`-filtered table (exclude `tenants`/`users`; `roles` = nullable-tenant special case).*
  2. *Route system/admin paths (`DbInitializer`, tenant lookup, system-context, cross-tenant Hangfire) to `ConnectionStrings:PrivilegedConnection` (BYPASSRLS `hrm_owner` from `roles.sql`).*
  3. *Flip `Rls:Enabled=true` + add CI RLS integration tests.*
  - ***Env precondition now MET*** *(native PG18 :5432 + Docker both up — the original "no Docker/Postgres" deferral reason is stale). QA-verified 2026-06-30: live DB has **0 policies / 0 RLS-enabled tables**, flag `false` → genuinely unimplemented (DB availability is NOT the blocker; the migration is). Completing it unblocks the `[DEFERRED]` isolation TCs ADM-ISO-016/020/024/027/031 + ADM-005-21. Run via `/implement-story US-PLT-002` (deliberate dev+review — RLS touches every tenant query).)*
- [x] US-PLT-003 — Serialize API enums as strings + reconcile FE enum casing *(PR #57: global JsonStringEnumConverter + recruitment FE casing; PR #111: leave-management + core-hr FE enum casing reconciled — **COMPLETE**)*
- [ ] US-PLT-004 — Observability & platform NFRs (OTel, health live/ready, per-tenant usage, SLOs) *(**net-new, reconciliation 2026-07-06, Theme I.** **UPDATE 2026-07-24 (feasibility study):** OTel instrumentation is now **coded but DORMANT** — `ObservabilityExtensions` wired + endpoint-gated (blank `OtlpEndpoint` ⇒ Console-only, no backend); traces AspNetCore/HttpClient/Npgsql/Redis + runtime metrics. **Remaining:** stand up Grafana LGTM backend + Serilog→OTel sink + custom `HRM.*` meters + health live/ready; US-ADM-002 KPIs still hardcoded null. See [observability plan](../Architecture/observability-otel-grafana-plan.md). Underpins US-ADM-002 + US-ADM-012.)*
- [x] US-PLT-005 — Encryption-at-rest for sensitive PII & MFA secrets *(**DONE 2026-07-29 — but not as written.** The 2026-07-06 stub was factually stale: **AC-1 was already built** (`users.mfa_secret` is Data-Protection-wrapped with a Postgres-persisted key ring since PR #224 / ISSUE-247 — it was never plaintext at the cited line), and **AC-2 is N/A by design** (`users.mfa_secret` is the ONLY secret column in the schema; per-tenant SMTP/IdP secrets do not and will not exist — [ADR 2026-07-29](../vault/decisions/ADR-2026-07-29-tenant-secrets-are-platform-level.md)). **AC-3/AC-4 delivered** by PR #273/#377/#438. The one real gap — legacy plaintext rows silently tolerated forever by `Unprotect` — is closed by **Scope A**: `IFieldProtector.IsProtected`, an idempotent startup back-fill, and a system-scope legacy count on the encryption report. Mutation-verified 3 ways. Story doc rewritten.)*
- [x] US-PLT-006 — Error tracking via self-hosted GlitchTip (Sentry-API-compatible) *(**✅ DONE — PR #448 (2026-07-25): Sentry.AspNetCore/Serilog 6.7.0 wired (AC-1..5, inert-when-blank DSN, BeforeSend PII scrub, tenant tags) + `@sentry/angular` 10.68.0 FE slice (AC-6); story `docs/BA/platform/US-PLT-006.md` + 8 TCs. AC-7 (gt-pgdata in a backup routine) DONE — platform backup routine stood up at `ops/backup/` (dumps app+Hangfire DB + GlitchTip DB, retention + restore), smoke-tested; [[ISSUE-330]] RESOLVED. Real GlitchTip DSN + `docker compose up` remain an ops step.** net-new 2026-07-24, from the error-monitoring feasibility study.** DECIDED self-hosted per [ADR 2026-07-08](../vault/decisions/ADR-2026-07-08-saas-data-governance-posture.md); scaffolded at `ops/glitchtip/` but **0% wired** (no `Sentry.*` pkg / no DSN). **Scope:** `Sentry.AspNetCore` + `Sentry.Serilog` sink + mandatory `BeforeSend` PII scrub + `tenant_id`/`tenant_subdomain` tag (BE); optional `@sentry/angular` (FE); run the compose. **AC:** a thrown exception surfaces in GlitchTip with stack+release, tagged by tenant, with request-body / `Authorization` / email PII scrubbed; blank DSN ⇒ inert. **Recommended FIRST of the monitoring work** — highest value/effort, PII stays in-boundary; `Sentry.AspNetCore 6.6` supports .NET 10. Full sketch: [observability plan Phase 5](../Architecture/observability-otel-grafana-plan.md). **Datadog rejected** (cloud PII egress). Full US-830 doc TBD — run `@business-analyst` if a formal story file is wanted before `/implement-story US-PLT-006`.)*

### QA-Surfaced Dev Backlog (from 2026-06-30 isolation + FE testing — fixes/implementations needed to unblock tests)
> These are dev tasks (fixes or unbuilt features) found during the P3 testing campaign. Full detail in [docs/QA/TEST-FINDINGS.md](../QA/TEST-FINDINGS.md). Hand to a fix cycle / `/implement-story`; not auto-picked.
>
> **⚠ RECONCILED 2026-07-28** — this section was badly stale: it listed 18 items as open that the #119–#382 campaigns had already fixed. Each was re-checked against its finding status in `TEST-FINDINGS.md`. **Only 2 items remain open.** The cleared items are kept below (struck through, with their closing PR) so the section stays auditable rather than silently shrinking.

**▶ STILL OPEN (2):**
- [ ] **FIX BUG-098 (MED, FE)** — `getContrastTextColor(hex)` in `leave-type.models.ts:127-128` calls `.replace('#','')` with no null guard; `leave_types.color` is nullable (8 of 13 null for acme) → `TypeError` per null-color row on the Leave-types config page **and** the employee leave-application picker. One-line null-coalesce + a spec arm feeding `color: null`. US-LV-001 / US-LV-003.
- [ ] **BUILD deferred Admin monitoring KPIs** (TC-ADM-002-14..18 `[DEFERRED]`) — aggregate error-rate %, P95 latency, SLA-uptime %, storage/API-call/email usage gauges. **Blocked on US-PLT-004** (the metrics don't exist until the OTel meters + LGTM backend do); US-ADM-002 currently returns hardcoded nulls.

**✅ CLEARED (verified against TEST-FINDINGS.md, 2026-07-28):**
- ~~BUG-003 (CRIT cross-tenant JWT-vs-subdomain)~~ RESOLVED PR #119 — systemic `TenantAccessGuardMiddleware`. *(Formal closure still wants a live `/verify-fix --iso` re-run — parked as a verify task, not a build task.)*
- ~~BUG-107 (HIGH impersonation blocklist)~~ RESOLVED PR #125 · ~~BUG-106 (suspended-tenant 451 exemption)~~ RESOLVED PR #130
- ~~BUG-104 + ISSUE-217 (`/tenant/exports` route mismatch)~~ RESOLVED PR #146 / #331
- ~~BUG-097 (session restore)~~ #260 · ~~BUG-099 (Directory crash)~~ #132 · ~~BUG-100 (Custom Fields crash)~~ `46d7ebb2` · ~~BUG-101 (carry-forward NaN)~~ #159 · ~~BUG-102 (apply-leave dropdown)~~ #145
- ~~Systemic a11y classes~~ — BUG-096 RESOLVED #368 (contrast token); BUG-108/109/110/111/112 RESOLVED #295. *(Residual: **ISSUE-296** — ~16 hand-rolled `role="dialog"` overlays still lack a shared focus-trap wrapper; tracked in the P7 tail, not here.)*
- ~~BUG-113 (employee↔location `LocationId`)~~ RESOLVED #261 · ~~BUG-114 (`MaxStorageGb` quota)~~ RESOLVED #332 · ~~ISSUE-218 (reporting chain)~~ RESOLVED via DF-8 #410
- ~~US-PLT-002 RLS~~ — code COMPLETE, policies proven on real Postgres (Docker + native), committed **OFF**; the 19 `[DEFERRED]` isolation TCs are unblocked whenever the flag is flipped. Prod flip is an **ops** step, not a dev task.

## 6. Payroll (13 stories) — COMPLETE ✅
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
- [x] US-PAY-013 — Full & Final (F&F) settlement — **Phase 1 shipped** *(PR #303)*; Phase 2 (gratuity/notice/severance/loan recovery, settlement PDF, FE policy UI) deferred

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
- [x] US-NTF-006 — Notification delivery layer (SMTP email + SignalR/in-app dispatch) *(**net-new, Theme B** — SHIPPED across 8 phases: #216-#220 payroll/recruitment/performance, #265-#268 attendance/core-hr/report+import. `RealNotificationDispatcher` (Program.cs) + all feature services registered as `Real*` (DependencyInjection.cs); in-app SignalR always real, email real via `SmtpEmailSender` when `Smtp:Host` set (else graceful `LogOnlyEmailSender`). Verified 2026-07-19: 24 of 27 dependent delivery triggers WIRED at the call-site; 3 residuals → DF-40/41/42, 1 moot (TOTP).)*

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

> **RECONCILED 2026-07-28** (counts re-derived mechanically from the per-module rows above, which remain the
> source of truth). Prior tally text claimed "In progress: 1 / 5 net-new remain" while 6 rows sat at `[~]` and
> the SSO + location-calendar epics had shipped unrecorded. Current mechanical count: **`[x]` 120 · `[ ]` 4 · `[~]` 1**.

**▶ Everything remaining, in one place:**
| # | Item | Kind | Blocked on |
|---|------|------|-----------|
| ~~1~~ | ~~**US-PLT-005**~~ — ✅ **DONE 2026-07-29** (Scope A; AC-1/3/4 were already built, AC-2 closed N/A by ADR) | net-new story | — |
| 2 | **US-ADM-012** — plan/module governance enforcement (403 disabled-module APIs, usage limits) | net-new story | — |
| 3 | **US-PLT-004** — observability NFRs (LGTM backend · Serilog→OTel sink · `HRM.*` meters · health live/ready) | net-new story | — *(unblocks US-ADM-002 KPIs)* |
| 4 | **US-PRF-011** — performance calibration workspace | net-new story | — *(unblocks US-PRF-010 dead-end)* |
| 5 | **US-PLT-002** — RLS prod flip | **ops**, not dev | user's deploy step (`Rls/README.md` §3b) |
| 6 | ~40 `[x]` stories with **unbuilt ACs** | deferred ACs | see the Deferred-AC table below |
| 7 | P6 deferred FE (ISSUE-271/272/267), P7 LOW tail (~150 LOW + 20 ENH), BUG-098 | finding-driven | `docs/QA/TEST-FINDINGS.md` |

- Total stories: **105 spine-done** + **10 net-new backlog** (reconciliation 2026-07-06) = **115 tracked**.
- Done spine: **103** — **Authentication (10)**, **Core HR US-CHR-001..012**, **Leave US-LV-001..012**, **Attendance US-ATT-001..010**, **Recruitment US-REC-001..010**, **Payroll US-PAY-001..012** (PR #63–#74), **US-PLT-001** (#50), **Performance US-PRF-001..010** (#75–#84), **Admin Console US-ADM-001..010** (#85–#94), **Onboarding US-ONB-001..006** (#95–#100), **Notifications & Audit US-NTF-001..005** (#101–#105), **Reports & Analytics US-RPT-001..005** (#106–#110).
  - ⚠️ **BUT** ~40 of these `[x]` stories carry **unbuilt ACs** — see the **Deferred-AC Reconciliation** table below. They are not fully done; the spine is.
- In progress: **1** (US-PLT-002 — RLS code complete + proven, committed OFF; only the **ops** prod flip remains).
- **Net-new backlog (2026-07-06 reconciliation): 10 → 6 shipped, 4 remain.** SHIPPED: US-ADM-011 (#238-240), US-TRN-EPIC/001/002/003 (#241-243), US-NTF-006 (8 phases), US-PLT-006 (#448/#449). REMAINING `[ ]`: US-PLT-005 / US-ADM-012 / US-PLT-004 / US-PRF-011.
- **Also shipped since the last tally (were never recorded here):** the **SSO epic** US-AUTH-011..016 (#112/#444/#446/#447/#450) and the **location-calendar epic** US-ATT-011 + US-CHR-013 (CAL-1..8, #310-#318).
- **Recommended next build order (updated 2026-07-29):** ~~US-PLT-005~~ ✅ done → **US-ADM-012** (self-contained; US-ADM-009 config already exists, just unenforced) → **US-PLT-004** (unblocks the US-ADM-002 KPI TCs) → **US-PRF-011**. Then the finding-driven tail (P6 FE, P7 LOW). ⚠ **Note:** the US-PLT-005 experience — a stub story whose premises had gone stale — is a standing warning. **Grep the code per-AC before building any of the three remaining stubs**; US-ADM-012 has already been confirmed ~45% stale the same way (BUG-114 storage quota and the custom-field cap are enforced, not unenforced).

## Deferred-AC Reconciliation (2026-07-06) — `[x]`-done stories with UNBUILT acceptance criteria

> Source: [docs/QA/COMPLETION-PLAN-2026-07-06.md](../QA/plans/archive/COMPLETION-PLAN-2026-07-06.md) **PART II** (Themes A–M).
> These stories stay `[x]` — their **data-layer spine is built and wired** — but the listed ACs/FRs are
> genuinely unimplemented (almost all *outward delivery* or *cross-module seams* stubbed before the dependency
> existed and never rewired). This is a **status-integrity** annotation, not a re-open. Where a deferred AC is
> unblocked by a net-new story, that story is named. **Do not re-mark these fully done until the noted ACs ship.**

| Story | Deferred AC / FR (unbuilt) | Why / Theme | Unblocked by |
|---|---|---|---|
| US-AUTH-001 | **lockout email now real-delivered ✅ (#384)** — `LockoutNotificationService` migrated onto the NTF-006 dispatcher (DF-40 DONE); login **still not** rate-limited. *(password-reset email also real-delivered ✅ NTF-006)* | rate-limit absent (D) | — |
| US-AUTH-002 | AC-7 JWT key rotation/overlap — single static signing key | no rotation (D) | — |
| US-AUTH-004 | password-history configured-but-**unenforced**. *(reset-email now real-delivered ✅ NTF-006)* | D | — |
| US-AUTH-005 | challenge **not** rate-limited. ~~MFA secret stored **plaintext**~~ — **FALSE, corrected 2026-07-29**: it is Data-Protection-wrapped (PR #224); legacy rows now auto-healed (US-PLT-005 Scope A). *(MFA is TOTP — no server-side code to deliver; the "delivery" gap was moot)* | D | — |
| US-AUTH-007 | FR-9 subdomain cache **not** invalidated on tenant status change (suspended tenant resolves Active for TTL) | D | — |
| US-AUTH-015 | per-tenant SSO gating + `sso_only` UX deferred | lands with US-AUTH-012/016 | US-AUTH-012/016 |
| US-CHR-001 | BUG-113 `LocationId` not wired (employee↔location link impossible). *(probation-ending notification now real-delivered ✅ NTF-006)* | E functional gap | — |
| US-CHR-002 | **Education / Work-History / Dependents backend now EXISTS ✅ (#386)** — net-new `EmployeeEducation` / `EmployeeWorkHistory` / `EmployeeDependent` entities+tables + address columns + CRUD + `PATCH {id}/profile` save path; FE edit re-enabled off the #380 read-only state (DF-38/DF-39 DONE). Employment (dept/title/type/status) + address edit fields wired (ISSUE-320 addressed). Profile-edit **route** 404 fixed #380 (ISSUE-319 RESOLVED). | E functional gap | — |
| US-CHR-008 | EXIF not stripped from photos; magic-byte sniff (BUG-058). *(doc-expiry notification now real-delivered ✅ NTF-006)* | D | — |
| US-CHR-010 | **custom-field columns in import (FR-11)** — see story AC-K1. *(import-completion notification now real-delivered ✅ NTF-006)* | K | — |
| US-CHR-011 | ~~reporting-manager/chain not on `GET /employees/{id}` (ISSUE-218)~~ **RESOLVED via DF-8 (#410)** — full reporting-chain now on `GET /employees/{id}`. *(manager-reassignment notification now real-delivered ✅ NTF-006)* | ~~E~~ ✅ | — |
| US-CHR-012 | custom-field **cap not enforced**; custom-fields absent from bulk import | H; K | US-ADM-012 |
| US-LV-002 | **FTE proration (BR-2)** + **accrual-frequency scheduling (FR-5)** — see story AC-K1/K2 | K | — |
| US-LV-005 | **AC-4 multi-level routing inert** (`WorkflowInstanceId` null); **BR-4 payroll-lock hardcoded false**. *(approval/reject email now real-delivered ✅ NTF-006)* | C/E; E | US-ADM-011 |
| US-LV-010 | AC-4 cancellation ignores payroll lock (always "not locked") | E | US-ADM-011 |
| US-LV-011 | **AC-2 auto-LOP inert** — behind `NoOpAttendanceProvider` | E | (attendance provider wiring) |
| US-LV-012 | **FR-1 Dept Leave-Coverage report returns empty** — see story AC-K1 | K | — |
| US-ATT-003 | UTC-only day-boundary/late detection (wrong for non-UTC tenants). *(regularization request-notification now real-delivered ✅ NTF-006)* | J (ISSUE-065) | — |
| US-ATT-004 | **AC-4 multi-level regularization approval inert**. *(approval/reject notification now real-delivered ✅ NTF-006)* | C | US-ADM-011 |
| US-ATT-008 | UTC-only late/early detection (ISSUE-065). **FR-7 chronic-lateness escalation shipped ✅ (#385).** *(late-arrival alert now real-delivered ✅ NTF-006)* | J | — |
| US-REC-002 | resume magic-byte sniff (BUG-058). *(application-confirmation email now real-delivered ✅ NTF-006)* | D | — |
| US-REC-005 | **interview-guide attachment (FR-8)** — see story AC-K1. *(interview-schedule notification now real-delivered ✅ NTF-006)* | K | — |
| US-REC-006 | **scorecard versioning** — see story AC-K1. *(scorecard-submitted email now real-delivered ✅ NTF-006)* | K | — |
| US-REC-007 | **offer magic-link now embedded ✅ (#384)** — email+PDF real-delivered and a portal token is now issued/embedded at offer-send via `PortalLinkBuilder` (DF-42 DONE); **FR-10 offer-approval routing** still inert | C/E | US-ADM-011 |
| US-REC-008 | **status-tracking magic-link email now delivered ✅ (#384)** — applicant-portal token minted+persisted and the delivering email now fires through the real dispatcher (`applicant_portal_link` event / `PortalLinkBuilder`); DF-41 DONE | — | — |
| US-REC-010 | **AC-3 no user-account creation** *(partially shipped — FR-5 provisioning #355)*, **AC-2 no salary persistence, AC-4 no "Converted" badge (ISSUE-232)**; **FR-9 welcome-email + FR-8 onboarding trigger still deferred/log-only** (ISSUE-140 residual — only the generic "Converted" stage-change email fires) | E; B (ISSUE-140) | ISSUE-140 |
| US-PAY-009 | **year-end tax-statement PDF (ISSUE-177)** + report PDF export | F | — |
| US-PRF-001 | **goal-set finalize == 100% shipped ✅ (#387, BUG-056)** — `POST /tenant/performance/goals/finalize` locks the set to `GoalStatus.Finalized` (409 `goals_finalized` thereafter); goal-**read** authz self-scoped (#387/DF-18); re-open endpoint shipped ✅ **DF-46 (#393)**. See story AC-K1/K2. *(goals-set notification also real-delivered ✅ NTF-006)* | K (DONE) | — |
| US-PRF-002 | **AC-B1 self-assessment attachment DELETE missing (BUG-243)**. *(self-rating notification now real-delivered ✅ NTF-006)* | F/BUG-243 | — |
| US-PRF-004 | **AC-B1 cycle rating-scales endpoint missing**; **AC-B2 low-privilege "resolve active cycle" resolver missing — cross-cutting BUG-243 enabler** | F/BUG-243 | — |
| US-PRF-005 | **360 report PDF**; **AC-B1 reviewer full-replace PUT · AC-B2 standalone tracker · AC-B3 get-form-by-assignment missing (BUG-243)**. *(360 reviewer-assigned notifications now real-delivered ✅ NTF-006)* | F; F/BUG-243 | — |
| US-PRF-006 | **review meeting PDF** | F | — |
| US-PRF-007 | **dashboard PDF export** | F | — |
| US-PRF-008 | **PIP PDF**; **AC-B1 PIP draft/pre-fill endpoint missing (BUG-243)**. *(PIP-initiated notification now real-delivered ✅ NTF-006)* | F; F/BUG-243 | — |
| US-PRF-010 | **recommendation PDF**; **calibration dead-end trap** (permanent lockout); **AC-B1 completed-cycles picker missing (BUG-243)**; **AC-B2 team-recs = workspace reshape (BUG-243, not a gap)** | F; E; F/BUG-243 | US-PRF-011 |
| US-ADM-002 | monitoring KPIs (error-rate/latency/SLA/usage) **hardcoded null** | I | US-PLT-004 |
| US-ADM-006 | plan-gated enterprise-only settings absent (#17) | H | US-ADM-012 |
| US-ADM-009 | module-gating **not enforced** (disabled-module API not 403'd, no FE guard); usage limits config-only (BUG-114) | H | US-ADM-012 |

### Theme-K follow-up ACs attached to existing stories (see each story's "Follow-up ACs" section)
| Existing story | Attached follow-up | Finding |
|---|---|---|
| US-PAY-001 | ~~AC-K1 SalaryGrade entity~~ **DELIVERED #389** (entity + CRUD `/api/v1/tenant/salary-grades` + FE + JobTitle FK-validation) | ISSUE-021 |
| US-PRF-001 | ~~AC-K1 goal-set finalize (==100%)~~ **DELIVERED #387** (`POST goals/finalize` → `Finalized`/409; re-open = DF-46 ✅ **shipped #393**) | BUG-056 |
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
