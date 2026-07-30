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
- [x] US-PLT-004 — Observability & platform NFRs *(**DONE 2026-07-30 — real remainder only.** The stub was materially stale and was NOT rebuilt: AC-2 health live/ready was already built **and tested**, and `HrmCacheMetrics` already proved the `HRM.*` meter pipeline. Delivered: **Serilog→OTLP sink** (gated on `IsEnabled` AND a configured endpoint — console-mode deliberately does not register it, since that would ship logs at a collector that is not there) · **tenant span tags** (`tenant.id`/`tenant.subdomain`, null-safe because `Activity.Current` is null whenever OTel is inert, which is the default) · **`HrmDomainMetrics`** (login outcome, leave submitted, payroll-run duration). Earlier in the arc: [[ISSUE-345]] made OTel genuinely dormant + samplable and [[ISSUE-344]] made Redis health truthful. **Deliberately deferred as its own slice: the per-tenant API-call counter** — it needs a tenant-scoped aggregate table + dormant RLS policy + migration + a hot-path write, and an OTel counter alone is not queryable by `PlatformMonitoringService`, so a partial build would leave the `ApiCalls` gauge FAKE rather than honestly `Available:false`. Also closed [[ISSUE-346]]/[[ISSUE-347]]. Gate 4941/4941.)*
- [x] US-PLT-005 — Encryption-at-rest for sensitive PII & MFA secrets *(**DONE 2026-07-29 — but not as written.** The 2026-07-06 stub was factually stale: **AC-1 was already built** (`users.mfa_secret` is Data-Protection-wrapped with a Postgres-persisted key ring since PR #224 / ISSUE-247 — it was never plaintext at the cited line), and **AC-2 is N/A by design** (`users.mfa_secret` is the ONLY secret column in the schema; per-tenant SMTP/IdP secrets do not and will not exist — [ADR 2026-07-29](../vault/decisions/ADR-2026-07-29-tenant-secrets-are-platform-level.md)). **AC-3/AC-4 delivered** by PR #273/#377/#438. The one real gap — legacy plaintext rows silently tolerated forever by `Unprotect` — is closed by **Scope A**: `IFieldProtector.IsProtected`, an idempotent startup back-fill, and a system-scope legacy count on the encryption report. Mutation-verified 3 ways. Story doc rewritten.)*
- [x] US-PLT-006 — Error tracking via self-hosted GlitchTip (Sentry-API-compatible) *(**✅ DONE — PR #448 (2026-07-25): Sentry.AspNetCore/Serilog 6.7.0 wired (AC-1..5, inert-when-blank DSN, BeforeSend PII scrub, tenant tags) + `@sentry/angular` 10.68.0 FE slice (AC-6); story `docs/BA/platform/US-PLT-006.md` + 8 TCs. AC-7 (gt-pgdata in a backup routine) DONE — platform backup routine stood up at `ops/backup/` (dumps app+Hangfire DB + GlitchTip DB, retention + restore), smoke-tested; [[ISSUE-330]] RESOLVED. Real GlitchTip DSN + `docker compose up` remain an ops step.** net-new 2026-07-24, from the error-monitoring feasibility study.** DECIDED self-hosted per [ADR 2026-07-08](../vault/decisions/ADR-2026-07-08-saas-data-governance-posture.md); scaffolded at `ops/glitchtip/` but **0% wired** (no `Sentry.*` pkg / no DSN). **Scope:** `Sentry.AspNetCore` + `Sentry.Serilog` sink + mandatory `BeforeSend` PII scrub + `tenant_id`/`tenant_subdomain` tag (BE); optional `@sentry/angular` (FE); run the compose. **AC:** a thrown exception surfaces in GlitchTip with stack+release, tagged by tenant, with request-body / `Authorization` / email PII scrubbed; blank DSN ⇒ inert. **Recommended FIRST of the monitoring work** — highest value/effort, PII stays in-boundary; `Sentry.AspNetCore 6.6` supports .NET 10. Full sketch: [observability plan Phase 5](../Architecture/observability-otel-grafana-plan.md). **Datadog rejected** (cloud PII egress). Full US-830 doc TBD — run `@business-analyst` if a formal story file is wanted before `/implement-story US-PLT-006`.)*

### QA-Surfaced Dev Backlog (from 2026-06-30 isolation + FE testing — fixes/implementations needed to unblock tests)
> These are dev tasks (fixes or unbuilt features) found during the P3 testing campaign. Full detail in [docs/QA/TEST-FINDINGS.md](../QA/TEST-FINDINGS.md). Hand to a fix cycle / `/implement-story`; not auto-picked.
>
> **⚠ RECONCILED 2026-07-28** — this section was badly stale: it listed 18 items as open that the #119–#382 campaigns had already fixed. Each was re-checked against its finding status in `TEST-FINDINGS.md`. **Only 2 items remain open.** The cleared items are kept below (struck through, with their closing PR) so the section stays auditable rather than silently shrinking.

**▶ STILL OPEN (2):**
- [ ] **FIX BUG-098 (MED, FE)** — `getContrastTextColor(hex)` in `leave-type.models.ts:127-128` calls `.replace('#','')` with no null guard; `leave_types.color` is nullable (8 of 13 null for acme) → `TypeError` per null-color row on the Leave-types config page **and** the employee leave-application picker. One-line null-coalesce + a spec arm feeding `color: null`. US-LV-001 / US-LV-003.
- [ ] **BUILD deferred Admin monitoring KPIs** (TC-ADM-002-14..18 `[DEFERRED]`) — ⚠ **my own "Blocked on US-PLT-004" claim here was WRONG; corrected 2026-07-30 after verifying against the code.** Roughly 40% of this surface has no OTel dependency at all:
  - **Buildable TODAY, zero OTel dependency:** the **storage** gauge (`EmployeeDocumentService.cs:229-230` already does the per-tenant `SumAsync(FileSizeBytes)` — but see [[ISSUE-340]], it should widen to all four size-bearing tables) · the **email-sends** gauge (`NotificationDelivery` has `TenantId`/`Channel`/`Status`/`SentAt`; the `MaxEmailSendsPerMonth` key already exists) · **SLA-uptime** (needs only a probe-history table + a Hangfire job hitting the **already-built** `/health/ready`). That covers **TC-ADM-002-17 and two-thirds of TC-ADM-002-18**.
  - **Genuinely blocked on a metrics store:** aggregate error-rate %, P95 latency, 24h latency trend. *But* error-rate and top-errors may be sourceable from the **GlitchTip API** (already shipped, already tenant-tagged by `TenantTagSentryEventProcessor`) without standing up LGTM at all — validate that before committing to the Grafana phase.
  - **Needs new instrumentation:** the API-call counter (limit key exists, the counter does not).
  - ~~Also fix [[ISSUE-344]] first~~ → ✅ **FIXED 2026-07-30** — the dashboard now reports Redis's real health (and still reports NotConfigured when Redis genuinely is not configured, which is a supported mode here).

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
- [x] US-PRF-011 — Performance calibration *(**DONE 2026-07-30 — rescoped.** The stub's justification was **verified FALSE**: it claimed calibration "unblocks US-PRF-004/005/006/007", all four of which are `[x]` shipped. Nothing was built on that premise. The real gap was narrow: nothing distinguished an ORIGINAL rating from a CALIBRATED one, and there was no cohort surface. Delivered a separate **`RatingCalibration`** table (`OriginalScore` + `PreviousCalibratedScore` + `CalibratedScore` + `Reason` + `CalibratedByUserId`) — chosen over extra review columns because this is compensation-adjacent data where *"who changed my rating, when, why"* must be answerable across REPEATED rounds — plus a cohort query and a permission-gated apply-calibration command with a mandatory reason. Migration carries the dormant `tenant_isolation` policy; isolation is arm-tested. **[[ISSUE-349]] was WRONG and the gate caught it:** its premise that `IsCalibrationEnabled` and the Calibration phase must "agree" is false — the flag is a standalone feature toggle (the FE checkbox is literally labelled "Calibration phase") while `phases[]` is the timeline, so enabling calibration without a Calibration phase is the NORMAL shipped state. The symmetric rule broke `CycleCreateWirePayloadApiTests` (the BUG-257 real-payload regression) and would have 400'd every cycle-create where a user ticked that box. Kept only the coherent half. Gate 4954/4954.)*

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
- [x] US-ADM-012 — Plan/module governance enforcement *(**DONE 2026-07-30, six phases.** ⚠ The stub was ~45% stale — the storage quota and custom-field cap were already enforced, and 4 of 8 limit keys were live. **Phase 1** [[ISSUE-335]]: `tenants.enabled_modules` held TWO incompatible vocabularies (permission prefixes vs canonical keys) — a gate on that data would have 403'd every request for the seeded and E2E tenants. Seed fixed, CLI migration normalized live data, one fail-open predicate. **1b** FE `moduleGuard` + nav filtering + `module_not_entitled` branch. **2a** [[ISSUE-342]]/[[ISSUE-341]]: plan edits now propagate to running tenants, and `PUT /system/tenants/{id}/plan` exists — before this a gate would have enforced entitlements no admin could change. **2b** `ModuleEntitlementMiddleware` (positive route→module map, so unmapped routes fail open BY CONSTRUCTION). **3** [[ISSUE-338]]/[[ISSUE-339]]/[[ISSUE-340]] + `max_custom_roles`/`max_email_sends_per_month`. **4** real storage + email usage gauges; `ApiCalls` deliberately left unavailable pending US-PLT-004. Gates: 4829 → 4835 → 4866 → 4906 → 4931, all green.)*

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
| ~~2~~ | ~~**US-ADM-012**~~ — ✅ **DONE 2026-07-30**, six phases (normalization · FE gating · plan propagation + plan-change endpoint · module gate · limit keys · usage gauges) | net-new story | — |
| 3 | **US-PLT-004** — observability NFRs. ⚠ **Smaller than the stub implies:** AC-2 (health live/ready) already built + tested; `HRM.*` meters already started. Real remainder = LGTM stack, Serilog→OTel sink, domain meters, API-call counter | net-new story | — *(check GlitchTip overlap before standing up LGTM)* |
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
- **Recommended next build order (updated 2026-07-30):** ~~US-PLT-005~~ ✅ · ~~US-ADM-012~~ ✅ → **US-PLT-004** (real remainder only: Serilog→OTel sink · domain meters · per-tenant API-call counter · tenant span tags. **AC-2 health live/ready is already built and tested — do NOT rebuild it**; `HRM.Cache` proves the meter pipeline, so domain meters are an extension) → **US-PRF-011** (rescoped: calibrated-vs-original rating model + cohort surface; the "unblocker" AC was removed as fictional). Then the ~14 genuine deferred ACs — about half of which is ONE QuestPDF work item, not five — and the LOW findings band.

## Deferred-AC Reconciliation (2026-07-06) — ⚠ **~66% OF THIS TABLE IS STALE (swept 2026-07-30)**

> **Read this before planning off the table below.** A row-by-row verification against the code on 2026-07-30
> found that of **49** verifiable claimed-unbuilt ACs: **33 ALREADY-BUILT · 14 STILL-TRUE · 1 PARTIAL ·
> 1 UNVERIFIABLE**. The cause is dating: this reconciliation was written **2026-07-06**, a large batch of exactly
> these ACs landed on **2026-07-07 and 07-08**, and the table was never re-swept.
>
> **This is not only a planning problem — it concealed a defect.** [[BUG-291]] (HIGH, money) sat in this table
> for three weeks labelled *"AC-K2 accrual-frequency scheduling"*, an unbuilt convenience. It was actually a live
> over-credit: `AccrualFrequency` was ignored, a Monthly leave type credited a full year on the first accrual run,
> and that inflated balance was **encashed and paid out** in final settlements. Treat a "deferred feature" label
> in this table as unverified until the code confirms it.
>
> **Verified ALREADY-BUILT — do NOT rebuild these:**
> - **Security, 6 of 6 stale:** login rate-limiting (`AuthController.cs:42`, `cf5bb243`) · MFA-challenge
>   rate-limiting (`:433`) · JWT key rotation (`JwtKeyRingOptions`, `952b5fbe`) · password-history enforcement
>   (`AuthService.cs:865`, `fd99a3bb`) · subdomain-cache invalidation on status change · EXIF strip +
>   magic-byte sniff incl. the resume path (`ImageMetadataStripper`, `FileSignatureValidator`, `f806d890`;
>   BUG-058 was formally closed by QA on 2026-07-16 as *"already shipped (stale ledger)"*).
> - **Cross-module seams, 5 of 6 stale:** US-LV-005 AC-4 + BR-4 payroll-lock · US-LV-010 AC-4 · US-ATT-004 AC-4 ·
>   US-REC-007 FR-10. All wired by US-ADM-011 — **which this very file already states 100 lines above**.
> - **Capabilities:** US-LV-012 FR-1 Dept Leave-Coverage (real report, `7fd197a2`) · US-LV-002 FTE proration
>   (`6c9a8790`) · US-PAY-009 report PDF · US-PRF-007 dashboard PDF (#340) · US-ATT-003/008 tenant-tz day
>   boundaries · US-CHR-001 `LocationId` · US-CHR-012 custom-field cap · US-REC-010 AC-3 user-account creation
>   and AC-4 Converted badge.
>
> **Genuinely STILL-TRUE (~14):** custom-field columns in bulk import · accrual-frequency *(now [[BUG-291]], being
> fixed)* · auto-LOP still behind `NoOpAttendanceProvider` · interview-guide attachment · scorecard versioning ·
> US-REC-010 AC-2 salary persistence + FR-9/FR-8 welcome/onboarding · year-end tax **PDF** (the report itself is
> built) · 360 report PDF · review-meeting PDF · PIP PDF · recommendation PDF · US-ADM-002 monitoring KPIs ·
> US-ADM-006 plan-gated enterprise settings. **Roughly half of that is Performance PDF rendering — one QuestPDF
> work item, not five.**
>
> **Double-counted with the QA-Surfaced Dev Backlog above:** BUG-113 (`LocationId`) and BUG-114 (storage quota)
> appear as open here while struck through as RESOLVED in the same file.

### Theme-K follow-up ACs attached to existing stories (see each story's "Follow-up ACs" section)
| Existing story | Attached follow-up | Finding |
|---|---|---|
| US-PAY-001 | ~~AC-K1 SalaryGrade entity~~ **DELIVERED #389** (entity + CRUD `/api/v1/tenant/salary-grades` + FE + JobTitle FK-validation) | ISSUE-021 |
| US-PRF-001 | ~~AC-K1 goal-set finalize (==100%)~~ **DELIVERED #387** (`POST goals/finalize` → `Finalized`/409; re-open = DF-46 ✅ **shipped #393**) | BUG-056 |
| US-LV-002 | AC-K1 FTE proration · AC-K2 accrual-frequency scheduling | LV-002 BR-2/FR-5 |
| US-CHR-010 | AC-K1 custom-field columns in bulk import (spans US-CHR-012) | CHR-010/012 FR-11 |
| US-LV-012 | AC-K1 Dept Leave-Coverage report (empty stub) | LV-012 FR-1 |
| US-REC-006 | AC-K1 scorecard versioning | REC-006 |

### ~~BUG-243 follow-up ACs~~ — **OBSOLETE, 7 of 8 SHIPPED (verified 2026-07-30)**

> ⚠ **This table has been removed rather than maintained.** A verification sweep against the code found that
> **7 of its 8 rows shipped on 2026-07-08** — two days after the 2026-07-06 reconciliation that recorded them
> as "genuinely missing". Every "genuinely missing" verdict it contained was false except the rating-scales row.
> Keeping it would have kept sending people to rebuild working features.

| Story | AC-B | Verdict 2026-07-30 | Evidence |
|---|---|---|---|
| US-PRF-002 | AC-B1 self-assessment attachment DELETE | ✅ **BUILT** | `SelfAssessmentAttachmentsController.cs:105-111` · commit `bcd7c333` |
| US-PRF-004 | AC-B2 low-privilege active-cycle resolver | ✅ **BUILT** | `CyclesController.cs:53-54` (permission list widened) · `d2325a76` |
| US-PRF-005 | AC-B1 reviewer full-replace PUT | ✅ **BUILT** | `Feedback360Controller.cs:116` · `d2325a76` |
| US-PRF-005 | AC-B2 standalone tracker | ✅ **BUILT** | `Feedback360Controller.cs:139` · `d2325a76` |
| US-PRF-005 | AC-B3 get-form-by-assignment | ✅ **BUILT** | `Feedback360Controller.cs:158` · `d2325a76` |
| US-PRF-008 | AC-B1 PIP draft/pre-fill | ✅ **BUILT** | `PipController.cs:60-76` → `GetPipDraftQuery` · `536ac053` |
| US-PRF-010 | AC-B1 completed-cycles picker | ✅ **BUILT** | `RecommendationController.cs:48-57` + FE picker + tests · `bcd7c333` ([[ISSUE-352]]) |
| US-PRF-004 | AC-B1 cycle rating-scales endpoint | ⚠️ **PARTIAL / needs a read** | No dedicated route, but `RatingScaleMax` is already served on the low-privilege `GET /cycles/active` (`CycleDtos.cs:152`). Commit `536ac053` is titled *"…remove dead rating-scale picker"*, suggesting the FE need was deleted rather than served. **Read that commit before scheduling any work.** |

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
