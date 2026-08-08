# Pass B — doc→story coverage audit

> **Run:** 2026-08-08 · **Auditor:** `@requirements-auditor` contract · **Tree:** `test/local-subdomains` @ `923db177`
> **Question:** what does the technical document promise that no user story covers — and that no code implements?
> **Status:** ✅ VALIDATED — 2 of 2 orchestrator spot-checks confirmed, including one that **refuted the orchestrator's own headline finding**.

## Orchestrator validation

| Claim | Result |
|---|---|
| **Asset Management is COVERED — the orchestrator's brief was wrong** | ✅ **Confirmed, and the orchestrator's finding is retracted.** `HRM.Domain/Entities/Asset.cs` exists; `AppDbContext.cs:182` carries the tenant query filter (`// US-ONB-004: lite asset register for issuance tracking (tenant-scoped)`); issue endpoint `OnboardingAssetsController.cs:88`; return path `OffboardingController.cs:117` (`ReturnAssetCommand`); story `docs/BA/onboarding/US-ONB-004.md`. |
| Google sign-in genuinely absent | ✅ **Confirmed.** `grep -ril google` over `HRM.Api`/`HRM.Application`/`HRM.Infrastructure` returns only `bin/`, `obj/`, and a `Logs/` file — **zero source hits**. |

### ⚠ Retraction — the orchestrator's "free early signal" was wrong

Before this pass ran, the orchestrator reported Asset Management as a whole documented module at zero
coverage, on the basis that `docs/BA/asset-management/` and `HRM.Application/Features/Assets` do not
exist. **That conclusion was wrong.** Both path facts are true; the inference from them is not. The
capability ships in full — register, issuance, return, tenant isolation, and a story — it simply
lives under Onboarding/Offboarding, where it is actually used. The team even documented the choice:
`ModuleEntitlementMiddleware.cs:58` — *"Asset has no dedicated controller — it lives under
OnboardingAssetsController at /onboarding/assets."*

This is exactly the failure mode the `@requirements-auditor` contract warns about — *never infer from
a name* — applied in reverse: **inferring absence from an absent name.** Recorded here because the
same trap is what makes the existing ledgers wrong, and because the auditor catching its orchestrator
is the gate working as designed.

---

## SCOPE

Requirement source: `docs/Architecture/hrm_technical_document_v4.0.md` §3.1 (:144), §3.2 (:181), §5.1 (:259), §5.2 (:279), §11.1–11.14 (:776–878), plus §33 (:2700) which §11.13 delegates to. Compared against `docs/BA/INDEX.md`, `docs/BA/STATUS.md`, and **124 story files across 13 module directories**. Every capability with no obvious story was hunted in `src/` under ≥3 naming variants before any absence claim.

---

## VERDICT TABLE

`COVERED` rows are compressed where a whole section is clean; every `UNCOVERED-*` row is itemised.

### §3.1 In Scope — Authentication

| Req ID | Requirement | MoSCoW* | Verdict | Evidence | Note |
|---|---|---|---|---|---|
| 3.1-A1 | Local username + password | — | COVERED | US-AUTH-001 | |
| 3.1-A2 | Sign in with Microsoft (Entra) | — | COVERED | US-AUTH-011..016; `SsoController.cs` | |
| **3.1-A3** | **Sign in with Google** | **Should** | **UNCOVERED-AND-MISSING** | grep `google\|apple` over backend → **0 source hits**; FE → only unrelated placeholder text | §5.2:288 names a *specific* story for it |
| **3.1-A4** | **Sign in with Apple** | **Could** | **UNCOVERED-AND-MISSING** | same empty greps | §3.3:198 gates it on an Apple Developer subscription |
| 3.1-A5 | JWT access + refresh tokens | — | COVERED | US-AUTH-002 | |
| 3.1-A6 | MFA (TOTP), optional per tenant | — | COVERED | US-AUTH-005 | |

`docs/BA/authentication/US-AUTH-001.md:81` states verbatim: *"Social logins (Google, Microsoft, Apple) are deferred to a later phase."* Microsoft was later rescued by CR-AUTH-001 and given six stories. **Google and Apple were never picked back up** — that deferral note is their only trace in the entire BA corpus.

### §11.1 Platform — System Admin

| Req ID | Requirement | MoSCoW* | Verdict | Evidence | Note |
|---|---|---|---|---|---|
| 11.1-1 | Tenant lifecycle mgmt + impersonation | — | COVERED | US-ADM-001/003/004 | |
| 11.1-2 | Plans: catalog, entitlements, limits, migration | — | COVERED | US-ADM-009/012; `AdminPlansController.cs` | |
| 11.1-3a | Subscription view + plan change | — | COVERED | US-ADM-009 | |
| **11.1-3b** | **Credits, manual invoices, refunds, trial extension** | **Could** | **UNCOVERED-AND-MISSING** | grep `invoice\|refund\|trialextension\|TenantCredit` → only an email sample string | §3.2:182 defers *automated* billing, not manual ops |
| **11.1-4** | **Revenue dashboard: MRR/ARR, churn, plan distribution** | **Could** | **UNCOVERED-AND-MISSING** | grep `MRR\|ARR\|churn\|revenue` → zero functional hits | Also §33.3 |
| 11.1-5 | Domain registry, reserved subdomains | — | COVERED | US-ADM-001; `TenantResolutionMiddleware.cs:51` | Custom domains OUT-OF-SCOPE (§3.2:184) |
| **11.1-6** | **JWT signing-key rotation + refresh-token kill switch** | **Should** | **UNCOVERED-AND-MISSING** | grep `keyrotation\|rotate.*signing\|killswitch` → **0 hits**. Nearest is `AuthController.cs:401 RevokeAllSessions` = **per-user** | Incident-response primitive |
| **11.1-7** | **System users & roles (platform staff mgmt)** | **Should** | **UNCOVERED-AND-MISSING** | Roles seeded `PermissionCatalog.cs:333,343,378` (SystemAdmin/SystemSupport); no controller, no story | §4.2:228 defines the SystemSupport persona |
| 11.1-8 | Monitoring: health, usage, jobs, SLA | — | COVERED | US-ADM-002, US-PLT-004/006 | |
| 11.1-9 | Feature flags: global / per-plan / per-tenant | — | COVERED (per-plan + per-tenant) | US-ADM-012; `PlanFeatureFlagKeys` | **Global** flags unevidenced — minor |
| 11.1-10a | System lifecycle email templates | — | COVERED | US-NTF-002; `SystemNotificationTemplate.cs:17` | |
| **11.1-10b** | **Broadcast announcements** | **Could** | **UNCOVERED-AND-MISSING** | grep `broadcast` → one unrelated HR fan-out | |
| **11.1-10c** | **Maintenance mode** | **Should** | **UNCOVERED-AND-MISSING** | grep `maintenancemode` over BE+FE → **0 hits** | Operability gap, §6.12 |
| 11.1-11a | Cross-tenant audit log | — | COVERED | US-ADM-008 | *65% — distinct `system_audit_log` (§35.1) unconfirmed* |
| **11.1-11b** | **GDPR request management console** | **Could** | **UNCOVERED-AND-MISSING** (primitives exist) | Export ✓ `DataExportController.cs:11`; erasure ✓ `IAuditAnonymizationService.cs:4`. No intake/tracking, no story | §3.4:204 makes GDPR day-one |

### §11.2 Platform — Tenant Admin

| Req ID | Requirement | MoSCoW* | Verdict | Evidence | Note |
|---|---|---|---|---|---|
| 11.2-1..9,11,12 | Org profile · subscription · users/access · roles · auth policy · audit · module config · workflows · branding · localization | — | COVERED | US-ADM-005/006/007/008/010/011/012; US-AUTH-005/006/009/012; US-NTF-002/003 | Custom roles OUT-OF-SCOPE (Phase 2) |
| **11.2-7b** | **Tenant-configurable employment types** | **Could** | **UNCOVERED-AND-MISSING** | Hard-coded enum; `EmploymentType.cs` comment: *"Stored as an enum rather than a full entity in Phase 1."* | **Documented deliberate deferral, not a defect** |
| **11.2-10b** | **Optional per-tenant SMTP** | **Could** | **UNCOVERED-AND-MISSING** | grep `SmtpHost\|SmtpPort\|SmtpUser` over Domain+Application → **0 hits**. Only global `Smtp:Host` + per-tenant *From* address | `PayslipDistributionRunner.cs:37` admits the deferral |
| **11.2-13** | **Integrations: API tokens (Phase 1)** | **Should** | **UNCOVERED-AND-MISSING** | grep `apitoken\|personalaccesstoken\|ApiKey` → only GlitchTip's *outbound* token | Doc tags this **Phase 1**, unlike SSO/SCIM/webhooks |

### §11.3–11.9, 11.12, 11.14 — HRM modules

| Req ID | Requirement | Verdict | Evidence |
|---|---|---|---|
| 11.3 | Core HR incl. dependents/education/work history, org tree, grades, locations, custom fields | COVERED | US-CHR-001..013; `EmployeeDependent.cs`, `SalaryGrade.cs` (naming drift) |
| 11.4 | Leave incl. **encashment**, accrual, carry-forward, ledger, multi-level approval | COVERED | US-LV-001..012; `LeaveTypesController.cs:81` (`Encashable`) |
| 11.5 | Attendance incl. **selfie** + **IP allowlist** | COVERED | US-ATT-001..011; `AttendanceSettingsDtos.cs:77,82` |
| 11.6 | Recruitment incl. **public careers page** | COVERED | US-REC-001..010; `CareersController.cs:17-21` (`[AllowAnonymous]`) |
| 11.7 | Onboarding/offboarding, clearance, exit interview, F&F | COVERED | US-ONB-001..006, US-PAY-013 |
| 11.8 | Payroll incl. **bank advice** + **year-end tax statements** | COVERED | US-PAY-001..013; `PayrollReportsController.cs:147,51` |
| 11.9 | Performance incl. **goal cascading**, calibration, 360° | COVERED | US-PRF-001..011; `GoalCommands.cs:22` (`ParentGoalId`) — *70%, most likely row to degrade under Pass A* |
| **11.12** | **Asset register + issuance/return** | **COVERED** | **US-ONB-004/005**; `Asset.cs`, `OnboardingAssetsController.cs:88`, `OffboardingController.cs:117`, migration `20260617063946`, filter `AppDbContext.cs:182` |
| 11.14 | Settings & master data incl. custom dropdowns | COVERED | US-CHR-012; `CustomFieldService.cs:544,586` |

### §11.10 Training (Lite) · §11.11 Benefits

| Req ID | Requirement | MoSCoW* | Verdict | Evidence |
|---|---|---|---|---|
| 11.10-1 | Course catalog | — | COVERED | US-TRN-001; `TrainingController.cs:33-85` |
| **11.10-2** | **Session scheduling + per-session attendance** | **Could** | **UNCOVERED-AND-MISSING** | No `TrainingSession` entity; `TrainingCourse.cs` has flat `StartDate`/`EndDate` — one course = one implicit sitting |
| 11.10-3 | Evaluation + certification | — | COVERED (lite) | US-TRN-001 AC-8; `CourseEnrollment.cs:39` |
| 11.11-1 | Benefit plans, enrollment, dependents | — | COVERED | US-TRN-002/003 |
| **11.11-2** | **Reimbursement claims with receipts** | **Should** | **UNCOVERED-AND-MISSING** | grep `ReimbursementClaim\|ExpenseClaim` → **0 hits**. US-PAY-007 pays reimbursements *out* but has no claim/receipt/approval intake |

### §33 Reports & Analytics

| Req ID | Requirement | MoSCoW* | Verdict | Evidence |
|---|---|---|---|---|
| 33.1 | Tenant reports (headcount, joiners/leavers, attendance, leave, payroll, funnel, performance) | — | COVERED | US-RPT-001/002/003 |
| **33.1b** | **Training participation report** | **Could** | **UNCOVERED-AND-MISSING** | No RPT story names training; `TrainingController.cs:154` is per-employee only — *75%* |
| 33.2 | Tenant dashboards (HR/Manager/Employee) | — | COVERED | US-RPT-005 |
| **33.3a** | **MRR/ARR, churn, plan distribution, trial-to-paid** | **Could** | **UNCOVERED-AND-MISSING** | = 11.1-4 |
| 33.3b | Cross-tenant usage analytics | — | COVERED (partial) | US-ADM-002 |
| **33.3c** | **SLA breach reports per tenant** | **Should** | **UNCOVERED-AND-MISSING** | All `sla.*breach` hits are **approval-workflow** SLA — a different SLA. §1.4 criterion #4 (99.5%/tenant) is unmeasurable — *75%* |
| **33.3d** | **Storage growth + API call volume per tenant** | **Should** | **UNCOVERED-AND-MISSING** | `STATUS.md:111` self-reports the deferral honestly |
| **33.3e** | **Failed payment queue** | **Could** | **UNCOVERED-AND-MISSING** | Downstream of billing |
| 33.4 | Export CSV / Excel / PDF | — | COVERED | US-RPT-004 |
| 33.5 | Custom report builder | — | **OUT-OF-SCOPE** | §3.2:192 |

### §5.1 module rows with no 1:1 BA module

| Doc row | Verdict | Where it actually lives |
|---|---|---|
| **Audit** | COVERED (structural drift) | US-NTF-004/005, US-ADM-008 — no `docs/BA/audit/` |
| **Settings** | COVERED (structural drift) | Fanned across US-ADM-006/007, US-CHR-004/005/007/012, US-LV-001/007, US-PAY-001, US-ATT-005 |
| **Training** | COVERED-minus-sessions | `training-benefits` — one BA dir serves two doc modules |
| **Benefits** | COVERED-minus-reimbursements | `training-benefits` |
| **Platform — System Admin (§11.1)** | Maps to `admin-console` **only** | ⚠ The `platform` BA module (US-PLT-001..006) is **not** §11.1 — it is cross-cutting engineering (envelope unwrapping, RLS, enum casing, OTel, encryption, GlitchTip). §11.1's operator features rest entirely on `admin-console`, and **that is where 8 of the 15 gaps concentrate.** |

\* MoSCoW is the **auditor's inference** — the tech doc carries no MoSCoW tags. Inferred from §3.1 In-Scope membership, §5.2 sample-story mention, §1.4 success-criteria dependence, and downstream blocking.

---

## CONTRADICTIONS

**1. The orchestrator's brief was false on its headline claim.** See the retraction above. Correct verdict: Asset Management is **COVERED with deliberate structural drift**.

**2. Reverse drift, in the repo's favour.** SCIM is documented as **Phase 2** (§11.2:804) yet `ScimEntitlementMiddleware.cs:8` gates a live `/scim/v2` route prefix on a plan flag today. The tech doc is behind the code.

**3. No ledger contradiction on the gaps themselves — and that is the entire point of this pass.** `STATUS.md` claims 124/125 done. **Not one** of the 15 UNCOVERED-AND-MISSING items appears there as incomplete, because none of them has a story line to be marked incomplete on. The ledger is not lying here; it is *structurally incapable* of seeing these. Two are honestly self-reported in prose (`STATUS.md:111` on the API-call counter; `EmploymentType.cs` on the enum) — credit where due.

---

## GAPS RANKED

**P1 — Should, in-scope, named in the doc's own sample stories**

1. **Google sign-in** (3.1-A3) — §3.1:155 In Scope; §5.2:288 gives it a named user story; §3.3:199 assumes the Google dev account exists. Zero code, zero story. Close: one BA story + reuse the Entra OIDC pipeline (`SsoController`/`EntraSsoService` is generic OIDC — Google is a second provider config, not a second architecture) + the FE button. **Size: M**
2. **JWT signing-key rotation + refresh-token kill switch** (11.1-6) — incident response. §3.4:205 declares cross-tenant leak zero-tolerance; the containment lever for a leaked signing key does not exist. Close: system-admin rotate endpoint with an overlap window + a global refresh-token epoch bump. **Size: M**
3. **Platform staff user management** (11.1-7) — SystemAdmin/SystemSupport roles are seeded and enforced but nothing creates or manages the humans holding them. Close: `/api/v1/system/users` CRUD reusing the `TenantUsersController` shape against the system tenant. **Size: M**
4. **Tenant API tokens** (11.2-13) — uniquely, the doc tags this **Phase 1** while explicitly pushing SSO/SCIM/webhooks to Phase 2, so it is deliberately in scope and simply never storied. Close: hashed token entity + an auth handler alongside JWT bearer. **Size: M**
5. **Maintenance mode** (11.1-10c) — §6.12 Operability; needed for any migration requiring downtime. Close: a platform flag checked in `TenantResolutionMiddleware` returning 503 with a system-admin bypass. **Size: S**

**P2 — Should, blocks a documented success criterion**

6. **Per-tenant SLA breach reporting** (33.3c) — §1.4 criterion #4 is *"≥ 99.5% monthly, measured per tenant"*. Nothing measures it. `HealthProbe` + `AdminMonitoringController.cs:38` are the substrate. **Size: M**
7. **Per-tenant storage growth + API call volume** (33.3d) — already self-identified at `STATUS.md:111` with a sound rationale for not half-building it. Its absence also hollows out US-ADM-012's usage limits. **Size: M**
8. **Reimbursement claims with receipts** (11.11-2) — the only §11 module capability with no partial. §34:2 lists "expense" as a workflow-engine consumer, so US-ADM-011's runtime engine already has the approval machinery. **Size: M/L**

**P3 — Could, commercial surface (one coherent cluster, decision-gated)**

9–12. **Billing & revenue operations** — credits/manual invoices/refunds/trial extension (11.1-3b), revenue dashboard MRR/ARR/churn (11.1-4 = 33.3a), trial-to-paid conversion, failed payment queue (33.3e). §3.2:182 defers *self-serve signup with automated billing*, which is **not** the same as the manual billing ops §11.1:781 explicitly retains — but with billing handled offline in Phase 1 the operational cost is low. **Treat as one epic; confirm with the product owner whether Phase 1 billing ops stay in a spreadsheet. Size: L**

**P4 — Could, narrow**

13. **Training session scheduling** (11.10-2) — the doc says "(lite)" twice. **Size: M**
14. **Broadcast announcements** (11.1-10b) — `SystemNotificationTemplate` + the SignalR dispatcher make this cheap. **Size: S**
15. **Training participation report** (33.1b) — one aggregate query over `CourseEnrollment`. **Size: S**
16. **GDPR request management console** (11.1-11b) — both primitives ship; only intake/tracking is absent. **Size: S/M**
17. **Tenant-configurable employment types** (11.2-7b) — **a decision, not a defect.** Do not schedule without a customer asking. **Size: M**

---

## COVERAGE SUMMARY

Documented capabilities audited: **62** | COVERED: **45** | UNCOVERED-AND-MISSING: **15** | UNCOVERED-BUT-BUILT: **1** (SCIM, ahead of doc) | OUT-OF-SCOPE: **1** *(plus 3 §3.2 items correctly excluded inline)*

**Where the failures concentrate — three clean clusters, and none is an HRM module:**

- **§11.1 System Admin / operator surface: 8 of 15 gaps.** `admin-console`'s 12 stories cover tenant lifecycle and plans well, then stop. Everything the *platform operator* needs to run the business — revenue, billing ops, staff management, key rotation, maintenance mode, broadcasts, GDPR intake — is unstoried. The misleading part: a `docs/BA/platform/` directory exists and looks like it should hold this. It does not.
- **Platform observability/reporting (§33.3): 4 gaps.** Everything tenant-facing ships; everything operator-facing does not. Success criterion #4 is currently unmeasurable.
- **Auth breadth: 2 gaps.** Depth is excellent (16 stories, full Entra OIDC epic); breadth is not (2 of 4 promised providers absent).

**The 10 tenant-facing HRM modules are near-completely storied — 2 minor gaps across ~40 capabilities.** The doc→story pipeline worked for the product and skipped the platform business.

---

## CONFIDENCE

**Overall: 85%.**

- **95%+** — Google/Apple SSO, revenue/MRR, maintenance mode, broadcast, API tokens, reimbursement claims, per-tenant SMTP, credits/invoices/refunds, failed payment queue, storage/API volume. Multiple naming variants each, zero hits, several corroborated by explicit in-repo prose.
- **95%** — Asset Management is COVERED (orchestrator independently re-verified).
- **85%** — JWT key rotation missing (searched code only; could live in ops config/runbook). *Settled by:* checking `ops/` and key-management docs.
- **80%** — Platform staff user management missing. *Settled by:* enumerating `/api/v1/system/*` routes exhaustively.
- **75%** — Training participation report; **75%** — SLA-breach reporting (the SLA vocabulary is heavily overloaded by approval-workflow SLA, which could mask a platform-SLA surface).
- **70%** — Goal cascading COVERED. `ParentGoalId` gives goal→goal parenting; org/dept-tier owner semantics unverified. **Most likely row to flip to PARTIAL under a Pass-A depth audit.**
- **65%** — Cross-tenant/system audit log COVERED. §35.1 promises a distinct `system_audit_log` not confirmed as a separate table.

**What limited this pass:** it is a *coverage* pass, not a depth pass. A `COVERED` row means *a story exists and plausible code backs it* — **not** that the three-leg evidence bar is met. Several COVERED rows will likely degrade to PARTIAL under Pass A. Secondly, the working tree advanced mid-run (HEAD moved `26870bfb` → `923db177`) because a concurrent session is editing this repo.

---

## OUT-OF-LANE

- **type:** doc-drift · **severity:** MED · **where:** `hrm_technical_document_v4.0.md:804` vs `ScimEntitlementMiddleware.cs:8` · **what:** SCIM documented as Phase 2 but built and plan-gated at `/scim/v2` today. · **suggested-action:** move SCIM to Phase 1 or record an ADR; reconcile §3.2:183 ("SAML / SCIM… not built").
- **type:** doc-drift · **severity:** MED · **where:** `hrm_technical_document_v4.0.md:273` vs `docs/BA/onboarding/US-ONB-004.md` · **what:** §5.1 lists Asset Management as a top-level module; the BA delivered it inside onboarding/offboarding. Correct engineering call — but it is what made the orchestrator's brief conclude the module was missing. · **suggested-action:** annotate the §5.1 Asset Management row with "delivered under Onboarding/Offboarding — US-ONB-004/005". **The Audit and Settings rows have identical structural drift and will trip the next reader the same way.**
- **type:** risk · **severity:** MED · **where:** `docs/BA/platform/` (US-PLT-001..006) · **what:** the BA module named `platform` holds cross-cutting engineering stories, NOT the tech doc's "Platform — System Admin/Tenant Admin" (§11.1/§11.2). The name collision makes §11.1 look covered when its operator features rest solely on `admin-console`. · **suggested-action:** rename to `platform-engineering` or add a header note in `docs/BA/INDEX.md`.
- **type:** risk · **severity:** LOW · **where:** `AuthController.cs:401` · **what:** `RevokeAllSessions` is per-user; no platform-wide refresh-token invalidation lever exists. §3.4:205 makes cross-tenant leak zero-tolerance, so the containment tool for a compromised signing key is absent. · **suggested-action:** route to `/security-audit` for an exploitability rating before scheduling — it may outrank its inferred "Should".
