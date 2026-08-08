# Pass A7 — notifications requirements audit

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains` @ `afdc3911`
> **Depth:** 5 Must-Have stories at AC level (29 ACs) + 1 Should-Have at story level = **30 rows**
> **Status:** ✅ VALIDATED — 3 of 3 orchestrator spot-checks confirmed.
> **Headline:** 🔴 **the shipped default configuration sends no email at all** — including password reset and account lockout. And **this module refutes the codebase-wide FE/BE hypothesis**: zero contract mismatches found.

> ⚠ *This audit was recovered after the agent exhausted its 60-turn budget mid-research and returned no report. It was resumed with an instruction to emit from existing context and mark unreached ACs `UNVERIFIABLE` rather than infer them. `maxTurns` has since been raised to 140 (commit `fd0b99ce`).*

## Orchestrator validation

| Claim | Result |
|---|---|
| **Default deployment sends no email** | ✅ **Confirmed.** `appsettings.json:88` ships `"Host": ""`, and `DependencyInjection.cs:849-856` branches on it: blank → `LogOnlyEmailSender`. The config comment states it outright: *"BLANK Host => log-only stub (LogOnlyEmailSender), **the safe default that sends nothing**."* |
| `TEST-STATUS.md` calls US-NTF-006 "not-yet-built" | ✅ **Confirmed.** `TEST-STATUS.md:233-235` reads *"the **not-yet-built** US-NTF-006"* — while **34 files under `HRM.Tests/` reference it** and ~1,000 lines of delivery infrastructure ship. |
| `Employee` is `IAuditExempt` | ✅ **Confirmed.** `Employee.cs:12` — `public sealed class Employee : BaseEntity, IAuditExempt`. |

### 🔵 This module **refutes** the codebase-wide hypothesis — and that matters

Every brief in this exercise carried the pilot lesson: *the Angular layer is coded against a contract the API never had.* It held in five consecutive modules. **Here the auditor compared every FE model to its DTO field by field — `INotification`↔`NotificationDto`, `INotificationPage`↔`NotificationPageDto`, `ITemplateDetail`↔`TemplateDetailDto`, `ICategoryPreference`↔`CategoryPreferenceDto`, `IAuditLogEntry`/`IAuditLogDetail`↔their DTOs — and found ZERO mismatches.** URLs and verbs match the `[Http*]` attributes. **The Karma specs mock the real shapes.**

It also checked the specific trap I warned about (TCs fabricating a `system_audit_log` the way `TC-ADM-010-13` does) and found **the opposite**: `TC-NTF-004-11.md:40` explicitly handles the single-table reality as a CONDITIONAL, and `TEST-MATRIX.md:273` records the dedicated table as a deferred refinement. **This suite is honest on that point.**

**An auditor that reports the absence of the pattern its brief primed it to find is an auditor whose positive findings can be trusted.** This is the strongest calibration evidence in the whole exercise.

---

## VERDICT TABLE

| Req ID | Requirement (short) | MoSCoW | Verdict | Evidence (file:line) | Note |
|---|---|---|---|---|---|
| NTF-001 AC-1 | SignalR conn, JWT auth, joins `t:{tid}:user:{uid}` + role groups | Must | IMPLEMENTED | `NotificationHub.cs:44,47,64,69`; `Program.cs:157-162,660`; `notification.service.ts:54,93-99` | Tenant id from `ITenantContext`/JWT, **never client input** (`:83-88`) |
| NTF-001 AC-2 | Realtime push; badge increments; slide-in | Must | PARTIAL | `SignalRNotificationService.cs:86-88`; `notification.service.ts:101-103,171-179` | Functionally complete; the **"within 2 seconds"** clause is not statically verifiable |
| NTF-001 AC-3 | Bell dropdown: paginated, icon, title, message, relative time, read state | Must | IMPLEMENTED | `notification-bell.component.ts:34-80`; `NotificationsController.cs:32-45` | FE `INotificationPage` matches the DTO **field-for-field** |
| NTF-001 AC-4 | Click → mark read, badge decrements, navigate | Must | IMPLEMENTED | `notification-panel.component.ts:270-275`; `notification.service.ts:246-263,287-292` | Optimistic update + revert on error |
| NTF-001 AC-5 | Mark All as Read | Must | IMPLEMENTED | `notification.service.ts:266-285`; `NotificationsController.cs:81-91` | Contract matches |
| NTF-001 AC-6 | Cross-tenant isolation via group naming | Must | IMPLEMENTED | `NotificationHub.cs:44,56-62,83-88`; filter `AppDbContext.cs:741`; RLS policy + `Rls:Enabled=true` | Hub aborts unresolvable connections; **no client-callable join**. Test: `NotificationRlsPostgresTests.cs` |
| NTF-002 AC-1 | Template list per event type, default/custom | Must | IMPLEMENTED | `NotificationTemplatesController.cs:30-41`; `NotificationTemplateService.cs:41-69` | Catalog-driven |
| NTF-002 AC-2 | **Rich text editor** + placeholder panel + live preview | Must | PARTIAL (leg1) | `template-editor.component.html:129-136` (plain `<textarea>`), `:153-159`, `:173-202`; no Quill/TipTap dep | **The normative "Then" is fully met.** Only the editor affordance named in §8 is absent. *80% — defensible as IMPLEMENTED on a strict "Then"-only reading* |
| NTF-002 AC-3 | Save override; future emails use it | Must | IMPLEMENTED | `NotificationTemplateService.cs:120-143`; consumed at send time `RealNotificationDispatcher.cs:205-218` | Version auto-increments; reactivates soft-deleted overrides |
| NTF-002 AC-4 | Reset to default + audit | Must | IMPLEMENTED | `:202-215`; audit via `AuditCaptureInterceptor.cs:87-90,141-142` — `NotificationTemplate` is **not** `IAuditExempt` | |
| NTF-002 AC-5 | Tenant A's template invisible to Tenant B | Must | IMPLEMENTED | `AppDbContext.cs:747`; `SystemNotificationTemplate` deliberately unfiltered (`:202-204`) | **Correct split** — overrides filtered, system defaults global |
| **US-NTF-003** | Per-user preferences (story level) | Should | PARTIAL (leg1) | `NotificationPreferencesController.cs:32-80`; enforced at dispatch `RealNotificationDispatcher.cs:141-160`; route `app.routes.ts:705` | AC-1/2/3/5 ✓. **AC-4 not built** — per-tenant admin config of mandatory categories is explicitly out of scope, hard-coded (`NotificationPreferenceDefaults.cs:10-13`). FE/BE contract matches |
| **NTF-004 AC-1** | Employee update → audit row w/ action, resource, before/after, IP, UA, trace | Must | **PARTIAL (leg1)** | Generic path real `AuditCaptureInterceptor.cs:87-90,111-173`. But `Employee` is `IAuditExempt` (`Employee.cs:12`) → falls to `EmployeeFieldAuditLog`, which has **no action name, resource type/id, IP, UA or trace**. `grep "Employee.Update"` → **zero hits** | **The row is also invisible to the US-NTF-005 viewer**, which reads `AuditLogs` only |
| NTF-004 AC-2 | PII read audit naming fields + accessor + trace | Must | IMPLEMENTED (naming drift) | `PayrollAuditAction.cs:56,61,62,66`; `PayslipQueries.cs:72,105`; `EmployeesController.cs:74-78` | Doc says `.ReadSensitive`; code uses `.ViewSensitive`. **Field is named, value never logged** |
| NTF-004 AC-3 | Soft-delete → `.Delete` with before/after on `is_deleted` | Must | IMPLEMENTED | `AuditCaptureInterceptor.cs:141-142,175-183`; `LeaveRequest` is not exempt | |
| NTF-004 AC-4 | Auth events audited w/ IP, UA, status | Must | IMPLEMENTED | `AuthService.cs:3065,229,646,1045,1632,1909,248,494` | **Exceeds FR-3's list.** Tests: `AuthAuditWriteTests.cs`, `RefreshTokenReuseAuditTests.cs` |
| NTF-004 AC-5 | Tenant-scoped audit reads; RLS on `audit_log` | Must | IMPLEMENTED (naming drift) | RLS policy generated `…RlsPolicies_Dormant.cs:47-79`; enabled `DbInitializer.cs:149`; app-layer explicit filter (`AuditLogController.cs:14-15` — `audit_logs` has **no** EF query filter, confirmed) | AC names GUC `app.tenant_id`; code uses `app.current_tenant` |
| NTF-005 AC-1 | Paginated audit table, newest first, incl. IP | Must | PARTIAL | `AuditLogController.cs:41-67`; columns match DTO↔`IAuditLogEntry` | Only the **"within 2 seconds"** clause unverified |
| NTF-005 AC-2 | Filters applied; URL bookmarkable; count shown | Must | IMPLEMENTED | `:44-61,73-86,92-102`; `audit-log-list.component.ts:220,232-236` | FE filter model matches controller params **exactly** |
| NTF-005 AC-3 | Detail panel: before/after diff, full UA, trace | Must | IMPLEMENTED | `:108-119`; `SensitiveFieldMasker.cs:20-42`; FE diff helper | BE masks, FE diffs — documented and consistent |
| NTF-005 AC-4 | Export as **async Hangfire job** + in-app notification + **15-min signed URL** | Must | PARTIAL (leg1) | `AuditLogController.cs:127-140,146-154` — **synchronous** `File(...)`; `AuditLogDtos.cs:91-102` states the async path "is DEFERRED" | Real CSV/JSON export exists, filter-consistent and audited. **Documented deliberate deferral** |
| NTF-005 AC-5 | Tenant A admin sees no Tenant B rows | Must | IMPLEMENTED | Explicit filter + RLS; `Audit.View`/`Audit.Export` permission split | Auditor role read-only — matches BR-6 |
| **NTF-006 AC-1** | Real email enqueued; `LogOnly*` not wired in non-test env | Must | 🔴 **PARTIAL (leg1 — default config)** | `SmtpEmailSender.cs:25-98` (MailKit, real); DI gate `DependencyInjection.cs:849-856` | **Shipped default `appsettings.json:88` `Smtp:Host = ""` → `LogOnlyEmailSender` IS wired out of the box.** Config gate, not missing code — but **the AC is not met by a default deployment** |
| NTF-006 AC-2 | In-app row + SignalR push; independent channels | Must | IMPLEMENTED | `RealNotificationDispatcher.cs:53-75`, `:77-250`; `Program.cs:250-251,361` | Genuinely independent methods |
| NTF-006 AC-3 | Retry w/ exponential backoff, terminal `failed`, no exception to caller | Must | IMPLEMENTED | `SendEmailJob.cs:44,50` (`MaxAttempts=5`, delays 60/300/900/3600/21600), `:99-128` | **3-phase read/send/persist split is deliberately RLS-safe**; retry counter survives failure |
| NTF-006 AC-4 | Tenant template rendered; platform default fallback | Must | IMPLEMENTED | `RealNotificationDispatcher.cs:205-218`, tenant context restored `:100-111` | Missing template → `Failed` row with reason, **never a silent drop** |
| NTF-006 AC-5 | Opt-out suppresses; security types non-suppressible | Must | IMPLEMENTED | `:113-117,141-160`; `NotificationEventCatalog.cs` | BR-1 bypass explicit and catalog-gated, not ad-hoc |
| **NTF-006 AC-6** | Each tenant's own **sender identity** + template | Must | **PARTIAL (leg1)** | Template ✓, SignalR group ✓. **Sender identity ✗** — `RealNotificationDispatcher.cs:227-228` constructs `EmailMessage` with **no `FromAddress`**, so `SmtpEmailSender.cs:48-52` falls back to the global `Smtp:FromAddress` | Per-tenant From exists **only** on the payslip path (`Tenant.cs:281`). **Generic notification email is single-sender across all tenants** |
| NTF-006 AC-7 | ~30 `LogOnly*` seams now resolve the real dispatcher | Must | IMPLEMENTED | `DependencyInjection.cs:297,302,351,362,446,467,591,737,788,817,832` + `:210,213` — **all 12 module seams registered `Real*`**; `Program.cs:361` | **Brief's DF-40/41/42 residuals are stale — all three closed in #384** |
| NTF-006 AC-8 | Bulk fan-out as **individual retryable jobs** + **per-tenant rate limit** | Must | PARTIAL (leg1) | Batch summary ✓, per-recipient isolation + Polly ✓ (`PayslipDistributionRunner.cs:225-233`), per-send commit ✓. **Fan-out ✗** (one job loops in-process). **Rate limit ✗** (`MaxEmailsPerMinute = 0`) | **The resilience properties AC-8 cares about are met; the two named mechanisms are not** |

---

## CONTRADICTIONS

**1. `TEST-STATUS.md:233-235` calls US-NTF-006 "not-yet-built". It is built.** ~1,000 lines of delivery infrastructure (`RealNotificationDispatcher` 311 lines wired at `Program.cs:361`, `SendEmailJob`, `SmtpEmailSender`, `NotificationDelivery` + migration), all 12 producer seams registered `Real*`, and **34 files under `HRM.Tests/` reference it**. `docs/BA/STATUS.md:252` correctly marks it shipped. **The two ledgers contradict each other, and TEST-STATUS is the wrong one.**

**2. `TEST-STATUS.md` has no rows at all for US-NTF-002/003/004/005** — one story row total (`:211`, NTF-001) — while `docs/QA/notifications/` holds **60 authored TC files**. **Four Must-Have stories are invisible to the test tracker.**

**3. `TEST-MATRIX.md:216` asserts the platform does not use RLS.** Verbatim: *"isolates via EF Core global query filters … **NOT Postgres RLS** … both are deferred hardening."* Contradicted by `Rls:Enabled: true`, live policies for every `tenant_id` table including `audit_logs`, and the enable path in `DbInitializer.cs:149`. **TC-NTF-ISO-015 step 5 and TC-NTF-004-08 step 4 are marked CONDITIONAL against a mechanism that now ships.**

**4. Brief correction — DF-40/41/42 are not open.** `STATUS.md:252` says "3 residuals"; `DEFERRED-FOLLOWUPS.md:43-45` all read `✅ DONE (#384)`, and the code confirms.

---

## GAPS RANKED

1. **NTF-006 AC-6 — generic notification email has no per-tenant sender identity. M.** Every tenant's transactional email leaves from one global `Smtp:FromAddress`. Blast radius: **every email except payslips.** *Smallest fix:* resolve the tenant's configured From — **the field already exists** (`Tenant.cs:281`, used at `PayslipDistributionRunner.cs:376`) — and pass it into the `EmailMessage` at `RealNotificationDispatcher.cs:227`. `SmtpEmailSender.cs:48` already honours it.
2. **NTF-004 AC-1 — Employee changes produce a degraded audit row invisible to the viewer. M. Compliance-relevant.** *Smallest fix:* have `EmployeeService.cs:903` additionally write a structured `AuditLog` row with `Action="Employee.Update"` — **preferred over dropping the exemption**, which would reintroduce the double-write it exists to prevent.
3. **NTF-006 AC-1 — default deployment sends nothing. S (config).** Fix is ops, not code — **but the go-live checklist must make `Smtp:Host` a hard gate**, otherwise every "delivered" notification is a log line, *including password reset and lockout, which BR-1 designates non-suppressible.*
4. **NTF-005 AC-4 — export is synchronous. M.** Documented deferral. Real risk: a large-tenant export ties up a request thread. *Fix:* reuse the existing `AuditLogExporter` inside a Hangfire job and notify via `INotificationService` — **both already exist.**
5. **NTF-006 AC-8 — bulk send is one in-process loop with the rate limiter disabled. M.**
6. **NTF-003 AC-4 — tenant admins cannot configure mandatory categories. S. Decision, not defect.**
7. **NTF-002 AC-2 — HTML edited in a raw `<textarea>`. S. Lowest severity — the normative clause is satisfied.**
8. **Latency budgets unverified. —** Needs a running stack.

*Also noted, not AC-blocking:* NTF-002 FR-7 (custom sender domain + SPF/DKIM) has no implementation, only two comments referencing the risk. FR-5 language variants are enforced server-side but the FE offers no language switcher.

---

## COVERAGE SUMMARY

```
Requirements audited: 30 | IMPLEMENTED: 17 | PARTIAL: 9 | MISSING: 0 | CONTRADICTED: 4 (ledger-level; none lowered a code verdict)
```

**Where the failures concentrate — and it is NOT the usual place.** Every partial here is a **backend behaviour that stops short of the AC's named mechanism** (sender identity, async export, job fan-out, rate limit, SMTP config) or an audit-exemption side-effect. **Not a dead frontend.**

The brief's central hypothesis **does not hold for this module.** The stub-suspicion hypothesis **does**, but narrowly: exactly one `LogOnly*` remains reachable in production DI, and it is config-gated rather than hard-wired.

Leg 3 is strong — 60 IEEE-829 TCs plus xUnit references (NTF-006 ×34). **No AC failed on leg 3.**

---

## CONFIDENCE

**Covered thoroughly** (implementing code read end-to-end, both layers): NTF-001 (6 ACs), NTF-002 (5), NTF-005 (5), NTF-006 (8). **90%.**

**Covered with a caveat:** NTF-004. The interceptor, entity, auth call sites and RLS migration were read directly. **The full `IAuditExempt` list was NOT enumerated** — ~30 exempt types seen in a truncated grep, only `Employee` and `LeaveRequest` confirmed individually. AC-1 verdict **85%**; *a second pass should audit the exempt list for other entities whose "own writer" is as thin as `EmployeeFieldAuditLog`.*

**Story level only** (correct for Should Have, but shallower): NTF-003 — **85%**. Did not read the FE preferences template or the `ResetPreferencesCommand` handler body.

**Individually uncertain:**
- NTF-002 AC-2 — **80%.** Defensible as IMPLEMENTED on a "Then"-only reading; §8's explicit "use a WYSIWYG editor (Quill, TipTap)" was weighted as intent. **A human should settle whether the "When" clause is normative.**
- NTF-006 AC-1 — **75%.** Turns on whether "non-test environments" means *as shipped* or *as ops configures it*. The story's §10 supports the latter, which would make it IMPLEMENTED. *Settled by:* the production deployment's actual `Smtp:Host`.
- NTF-001 AC-2 / NTF-005 AC-1 — **95%** that the functional parts are built; the partial is purely the latency clause.

**Limits:** static reading only; all latency/throughput ACs asserted on mechanism, not measurement. No test executed — leg 3 recorded as *existing*, not *passing*. **Recovered from a turn-budget exhaustion**, so coverage is stated per-story above rather than assumed uniform.

---

## OUT-OF-LANE

- **type:** doc-drift · **severity:** HIGH · **where:** `TEST-STATUS.md:233-235` · **what:** declares US-NTF-006 "not-yet-built" while ~1,000 lines ship and 34 test files reference it; four Must-Have NTF stories have **no TEST-STATUS row at all** despite 60 authored TCs. · **suggested-action:** add rows for US-NTF-002..006, delete the "not-yet-built" clause, reconcile against `STATUS.md:252`.
- **type:** risk · **severity:** HIGH · **where:** `appsettings.json:88` · **what:** shipped default `Smtp:Host = ""` silently wires `LogOnlyEmailSender`, so **a default production deployment delivers no email at all — including password reset and account lockout, which BR-1 designates non-suppressible.** · **suggested-action:** add `Smtp:Host` to the go-live checklist as a hard gate; **consider a startup warning, or fail-fast in Production, when the log-only sender is selected.**
- **type:** doc-drift · **severity:** MED · **where:** `docs/QA/notifications/TEST-MATRIX.md:216` · **what:** states the platform isolates "NOT Postgres RLS" — contradicted by `Rls:Enabled=true` and the live policy/enable path. Two ISO TCs are CONDITIONAL against a mechanism that now ships. · **suggested-action:** re-run those ISO TCs under RLS-on and promote the conditional steps to hard assertions.
- **type:** risk · **severity:** MED · **where:** `Employee.cs:12` · **what:** `IAuditExempt` routes all employee changes to `EmployeeFieldAuditLog`, which lacks action/resource/IP/UA/trace and **is not readable from the audit-log viewer** — a compliance-visible hole behind an exemption whose justification ("has its own writer") is technically true but substantively weaker than the generic path. · **suggested-action:** review the full `IAuditExempt` list for other entities whose own writer produces a thinner row than `AuditCaptureInterceptor` would.
- **type:** doc-drift · **severity:** LOW · **where:** `STATUS.md:252` · **what:** claims "3 residuals → DF-40/41/42"; all three closed (PR #384). · **suggested-action:** update to reflect 27/27 triggers wired.
