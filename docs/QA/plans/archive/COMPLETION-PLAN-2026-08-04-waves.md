# ARCHIVED SNAPSHOT — P5 'next session' plan, Waves 0–6, 2026-08-04.

> Split out of [`../COMPLETION-PLAN.md`](../COMPLETION-PLAN.md) on **2026-09-01**, when the plan was
> audited and rebuilt. It carried five overlapping sections that each claimed to be 'the queue';
> this is one of them, preserved verbatim as history.
>
> **Not current. Do not execute from this file.** The live execution lane is
> [`../GAP-CLOSURE-QUEUE.md`](../GAP-CLOSURE-QUEUE.md); the current backlog is
> [`../COMPLETION-PLAN.md`](../COMPLETION-PLAN.md).

---

## 🎯 P5 — THE NEXT SESSION'S PLAN (2026-08-04, dependency-ordered)

> **Start here.** Ordered to avoid file conflicts and wrong-sequence rework, NOT by size. Verified against
> code 2026-08-04 — the previous deferred-AC list was ~10 items and half were already built.

### ⚠️ Conflicts this order exists to avoid
1. **The three `DF-61-conc-*` items all touch `PayrollRunProcessor`'s reprocess interlock** → ONE session, not three PRs.
2. ~~**[[ISSUE-358]] (`WhiteLabel`) and "US-ADM-006 plan-gated enterprise settings" are the SAME work** → do together or build the gate twice.~~ ✅ **MOOT — the gate was already built** (`520ef273`, 2026-07-31) and the ledger missed it. The conflict this rule existed to prevent cannot occur. The surviving residual is narrower: `WhiteLabel` gates only the primary colour, leaving logo/email-logo/favicon ungated on every plan.
3. **RLS prod flip must follow the latency-meter deploy** — `tenant_latency_bucket` ships a dormant policy the flip activates.
4. **Year-end tax PDF must REUSE `PerformancePdfRenderer`** (4 PDFs already ship through it) → don't start a second PDF stack.

### Wave 0 — ✅ DONE 2026-08-04 — deploy the latency meter
`docker compose build backend && docker compose up -d backend`. PR #458 is merged but the container predates it.
P95/trend need HISTORY — every hour it is not running is an hour missing from the first real reading. Migration
applies on startup.
**Outcome:** image rebuilt (the running one was built 04:18, PR #458 landed 07:05), container healthy in ~20s,
migration `20260804004010_Monitoring_TenantLatencyHistogram` applied, `tenant_latency_bucket` present.
**Verified live, not assumed:** 3 tenant-scoped requests → 3 counts in bucket 0 after one 10s flush interval, so
the middleware → `IApiCallCounter` → `ApiCallCounterFlushService` → `TenantLatencyUsage.UpsertAsync` chain is
actually recording. History is accumulating from 2026-08-04.

### Wave 1 — ✅ DECIDED 2026-08-04 (all four taken as recommended; the three gating ones are now unblocked)
| # | Decision | ✅ DECISION (2026-08-04) — recommendation was accepted as-is |
|---|---|---|
| D-a | **Clawback: [[BUG-291]] + [[BUG-293]] retroactive tail** | ✅ **DECIDED — absorb + fix forward + correct un-encashed balances.** **Absorb, fix forward, do NOT recover.** Both are overpayments to employees; recovering paid salary is legally fraught and trust-corrosive. **But decide on numbers, not instinct:** BUG-291's exposure report is built — run it, and build the matching BUG-293 query. **Split out** current employees with large *un-encashed* inflated balances: correcting a number before it becomes money is not clawback. ⏰ Open since 2026-07-30 — the only item accruing cost. |
| D-b | **[[ISSUE-358]] `WhiteLabel`** | ✅ **DECIDED — enforce it, bundled with US-ADM-006.** **Enforce it.** Deleting is easier and wrong — it removes a capability customers are sold rather than making it real. D3 set the pattern for Scim/CustomDomain/Sandbox. Do it WITH US-ADM-006's tenant-side `plan` block. |
| D-c | **[[ISSUE-203]] BCrypt workFactor 12** | ✅ **DECIDED — keep 12; ISSUE-203 stays OPEN pending a production-hardware re-measure (not a code task).** **Keep 12; re-measure on production hardware.** Lowering it makes a number look better by permanently weakening every existing hash, based on a measurement from a limited-core test host. |
| D-d | **`DF-61-conc-approval-race`** | ✅ **REFUSE-AND-TELL on `AwaitingApproval` + `Approved`.** The state-machine read was done before deciding (`PayrollRunStatus.cs`, `PayrollRunProcessor.cs:110-153`): the interlock today guards **only** `Finalized` and `Cancelled`, so a run under an in-flight approval — or one already **Approved but not yet Finalized** — can be silently reprocessed. `Approved` is the sharper hazard: reprocessing there changes what an approver signed off on. `Rejected` and `ReviewPending` stay re-runnable (HR corrects → re-submits → new workflow instance), so the legitimate correction loop is untouched. Silent un-submit was rejected: it discards an approver's decision without telling them. Implement as a 409 with a distinct error code alongside `run_finalized`/`run_cancelled`. |

### Wave 2 — isolated small fixes (no shared files; safe in parallel)
- **US-REC-010 FR-8/FR-9** — ★ **the deferral rationale EXPIRED.** `ApplicantConversionService.cs:36-37` claims *"there is no Onboarding module yet"* and *"welcome email: log-only seam"*. **Both false:** `OnboardingChecklistService`/`OnboardingTemplateService` exist and `IUserManagementNotificationService` → `Real...` (US-NTF-006). Wire conversion to them and DELETE the two lying comments. Cheap now, not blocked.
  - ⚠ **Correction to this plan's own estimate (2026-08-04):** FR-8 is cheap as written; **FR-9 is not.** FR-5 provisions a *passwordless User with an **Active** UserTenant*, and `InviteAsync` rejects active members (`UserManagementService.cs:359-362`), so the invitation rail cannot be reused — and per [[BUG-294]] that rail is dead anyway. **Design taken:** deliver FR-9 through `INotificationDispatcher` (the `RealTenantWelcomeEmailService` idiom) on a **new dedicated catalogue event key**, carrying a **`/forgot-password` link, not a one-time token**. Two reasons: it matches the platform's existing deliberate no-token decision for `tenant_welcome_*`, and a password-reset token lives **1 hour** (`AuthService.cs:659`) while a welcome email is typically sent days before the start date — a token-bearing link would be expired on arrival. Mutating the existing (dead) `onboarding_welcome` key was rejected: it is the dispatch job's **fallback** for unmapped types (`OnboardingNotificationDispatchJob.cs:99`), so adding credential placeholders there would render a credential template against unrelated payloads.
- [[ISSUE-194]] — one `GroupBy(a => a.DepartmentName)` at `LeaveReportService.cs:709`.
- [[ENH-024]] — FE `aria-describedby` on the disabled "Send payslips" button.
- `DF-plt-us002-fr3-drift` — doc only; US-PLT-002 FR-3 still prescribes the retired `SET LOCAL` GUC.

### Wave 2b — 🔴 [[BUG-294]] invitations are undeliverable (AUTO-HEALED IN 2026-08-04, promoted above Wave 3)
**Discovered while mapping FR-9's seam; grep-verified, not inferred.** `UserInvitation.TokenHash` is minted, stored and
rotated but **never read for verification anywhere** — and there is **no accept/activate endpoint**. Every invitation
email carries a live token to a route the backend cannot honour, so an invited tenant user can never log in; the admin
sees "sent" and nothing goes red. `InvitationStatus.Accepted` is unreachable code.
**Ranked HIGH and placed here** (severity × blast-radius): it silently breaks a core sold admin flow for *every* invited
user, and it is small — one endpoint that verifies the token, creates the `UserTenant` + `InvitedRoleIds` grants, flips
the row to `Accepted`, then hands off to the **existing** `reset-password` rail. Do **not** build a second
password-setting rail. Sequenced after Wave 2 only because Wave 2 is already in flight on its own branch.

### Wave 3 — ✅ payroll concurrency DONE 2026-08-04 (ONE branch; shared file, as planned)
`DF-61-conc-retry` + `DF-61-conc-approval-race` (after D-d) + `DF-61-conc-slip` together.
`DF-65-pg-encash` ✅ **DONE 2026-08-04 on its own branch — and it found [[BUG-296]] (MED), a real balance-overstatement bug.**
The encashable year-end path stamped its encash draw and its residual-expire draw with the IDENTICAL timestamp, and the
audit interceptor stamps one `CreatedAt` per batch — so both rows were byte-identical on both keys the running-balance
read sorts by, and Postgres could return either. The balance resolved to the intermediate post-encashment figure,
overstating the employee's remaining days. **InMemory could never have caught it** (full tick precision, insertion order
preserved) — the exact InMemory-masks-Postgres class this repo keeps hitting, and the reason the deferred item existed.
Fixed with the same `PoolRowTickOffset` idiom `LeaveRequestService` already used; the arm asserts a provider-independent
invariant (final balance == sum of amounts) rather than a hard-coded number.

**Outcome.** Two fixed, one closed-as-accepted:
- **`DF-61-conc-approval-race`** — both guard sites now share ONE `GuardNonReprocessableStatus` helper, because
  the *duplication* of the guard was the defect. `AwaitingApproval` + `Approved` refuse with distinct 409 codes;
  **`Rejected` deliberately stays re-runnable** (narrower than the ledger row proposed — HR corrects and
  re-submits a rejected run, and blocking that would break the correction loop).
- **`DF-61-conc-slip`** — a real-slip arm replacing the degenerate zero-employee proof. Mutation-verified against
  the actual failure mode: disabling the contended no-op fails it with *"expected 1, but found 2"*.
- **`DF-61-conc-retry`** — **accepted, not fixed** (your call). The hardening would make the entire payroll
  compute a retriable unit; that is a bigger money-path risk than a degradation that provably cannot lose money
  and is already backstopped by the marker + reconcile sweep. Reasoning and reopen-criteria recorded on
  `TryAcquireAsync` so it is not re-litigated from the shape of the code.

### Wave 4 — features

> ⚠️ **THIS SECTION WAS WRONG IN THREE PLACES. Re-verified against code 2026-08-04 (post-Wave-3) and corrected
> below.** Two of the three were *plan says pending, code says shipped*; the third was a factually false premise
> inside a recorded decision. The "verified still true 2026-08-04" stamp on the deferred-AC line did not survive
> re-checking — a reminder that a verification stamp is only as good as the depth of the check behind it.

- **[[ISSUE-359]]** file encryption at rest — the largest remaining engineering item. **Design decided
  2026-08-04:**
  - **Buffered AES-GCM, NOT streaming.** The finding's "files need STREAMING crypto" constraint was written
    from the shape of `IFieldProtector`, not from how callers behave. All **15** `OpenReadAsync` sites already
    buffer the whole file into a `MemoryStream`, and the biggest writes (payslip PDFs, exports) are already
    `byte[]` in memory *before* upload — so buffered crypto pins no memory that is not already pinned, while
    chunked-AEAD framing would be complexity **no caller exercises**. Upload caps are 5–25 MB per path.
  - **The envelope carries a version byte + key-id**, so a streaming v2 can land later *without rewriting
    already-encrypted files*. That is what makes starting buffered a reversible decision rather than a shortcut.
  - **Storage quota keeps measuring LOGICAL (plaintext) bytes.** `TenantStorageUsage` sums four DB columns all
    written from plaintext length and never stats the disk, so encryption does not break it — but disk will
    exceed the billed figure by the envelope overhead. Deliberate; to be commented at the site so it is not
    re-filed as drift.
  - ⚠️ **CORRECTION — the recorded rotation justification is FALSE.** The decision claimed the quarterly
    `EncryptionKeyAgeWatchdogJob` "already covers" a Data Protection scheme. It does not: that job reads
    `Encryption:ActiveKeyId` from `FieldEncryptionOptions` — the **AES-GCM config ring**. Data Protection is a
    separate mechanism with no `ActiveKeyId`, and nothing in this repo watches or re-encrypts against it.
    **Resolution:** rely on Data Protection's own automatic key roll with indefinite retention of old keys —
    which is exactly what the "never prune" constraint requires — and correct the finding text. No rotation
    tooling is built for files.
  - Remaining-plaintext count is surfaced **API-only**, mirroring the `MfaSecretsLegacyPlaintext` precedent
    (which likewise has no frontend).
  - Scope is genuinely small at the seam: **one** `IFileStorage` implementation, **18** injecting services /
    **32** call sites (the finding's "36 consumers" counted files containing the string, including the
    interface and XML doc comments). No call-site changes.

- **Deferred ACs — the line below was stale; re-verified 2026-08-04:**
  - **Custom-field columns in bulk import** — genuinely not built. Blocked in practice by
    `BulkEmployeeImportService`'s three parallel positional `string[]` arrays, which cannot carry a variable
    tail; they want to become one `record TemplateColumn(Name, Description, Sample)` list first. The validation
    seam already exists and is proven on the single-employee path.
  - **Interview-guide attachment** — genuinely not built (no field, no endpoint). ✅ **Its deferral rationale
    has EXPIRED**: the docs mark it CONDITIONAL on "File & Document Management (S26)", but `IFileStorage`,
    `IVirusScanner` and the `EmployeeDocumentService` upload/scan/store idiom all ship. Same expired-rationale
    class as US-REC-010 FR-8/FR-9.
  - **Scorecard versioning** — ⚠️ **the AC's premise is unbuildable as written.** It says "templates are not
    versioned", but `ScorecardCriteria` is a hard-coded static list of four criteria — there is no
    tenant-configurable template entity to version. **Decided 2026-08-04:** build the half that addresses the
    actual harm — append-only rating history instead of `ScorecardService`'s `RemoveRange` (which silently
    rewrites what a historical scorecard meant, on an `IAuditExempt` entity so nothing captures it) plus the
    missing `GET /scorecards/{id}` that blocks three test cases. Template versioning is re-filed as its own
    scoped story rather than smuggled in as a prerequisite.
  - **Year-end tax PDF** — ⚠️ **stale twice over.** There is **no live `deferred:true` stub** (no call site
    passes it; the parameter defaults to `false`), `BuildYearEndTaxStatementAsync` is fully implemented, and
    `PayrollReportRenderer` already renders it to PDF. The real gap is **US-PAY-009 AC-3**: *individual*
    per-employee statements with **month-wise** rows, available for **bulk download** — three things a tabular
    report cannot satisfy. **And "reuse `PerformancePdfRenderer`" was the wrong target**: it is an
    `internal static` class in the Performance namespace with four DTO-specific methods, so reuse would mean
    dragging a Payroll DTO into it or refactoring four shipped PDFs. **Decided: build on `PayslipPdfRenderer`**
    — same lane, `public` API, and structurally the same document (per-employee, per-period, branded,
    line-items). The "don't start a second PDF stack" warning is moot regardless: there are already **eight**
    QuestPDF renderers; the question was only ever which idiom to follow.

- ~~[[ISSUE-358]] implementation (after D-b)~~ — ⚠️ **ALREADY SHIPPED.** Commit `520ef273` (2026-07-31) added
  `PlanGatingDto`, the `Plan` block on `TenantSettingsDto`, `ResolvePlanGatingAsync`, and the BR-3 403
  (`plan_feature_locked`) in `UpdatePrimaryColorAsync`, with four unit arms. Verified: `520ef273` is an ancestor
  of HEAD. **The plan missed it because the work was folded into a commit titled for BUG-292** — the ledger-rot
  class already measured at 66% on 2026-08-03. Same for "US-ADM-006 plan-gated enterprise settings" in the
  Remaining-feature-work list above.
  **The genuine residual (decided 2026-08-04): `WhiteLabel` currently gates ONLY the custom primary colour.**
  Logo, email-logo and favicon uploads are ungated on every plan — so a tenant without the entitlement still
  white-labels most of what customers actually see, *including generated payslip and report PDFs*. That is the
  same theatre charge ISSUE-358 brought in the first place. **Gate the whole branding surface.** Two code
  residuals ride along: `ResolvePlanGatingAsync` bypasses `PlanFeatureFlagKeys` (a 4th seam not using the shared
  derivation the class exists to enforce) and runs a redundant DB query when `ITenantContext.FeatureFlags` is
  already populated per-request by `TenantResolutionMiddleware`.

### Wave 4f — ISSUE-359 file encryption at rest ✅ DONE (PR #469 + #470, 2026-08-05)
Uploads (`IFileStorage`) and report exports (`IReportExportStorage`) both sealed via Data-Protection
per-tenant purpose strings + an on-disk `MAGIC|VERSION|PAYLOAD` envelope; legacy plaintext still reads, so
no migration. Admin status + sweep endpoints back-fill the remainder.

**Two defects the 16 originally-passing tests could not see**, both caught by the auditors and worth
remembering as classes, not incidents:
1. *(integration-enforcer, CRITICAL)* The maintenance service scoped to the **ambient** tenant id. In system
   context `TenantId` is `Guid.Empty` while `IsResolved` stays **true**, so the platform-wide plaintext report
   would have read **0** for every operator who ever opened it, and the sweep would have sealed every file
   under a key derived from `Guid.Empty` — reporting success while making the estate permanently unopenable.
   Now resolved from disk (a de-provisioned tenant's directory still holds salary/PII; the tenants table
   would skip exactly those).
2. *(test-authenticator, HIGH)* Every arm built the decorator by hand, so **reverting the one DI line would
   have returned plaintext to production with zero tests failing.** DI-wiring guards added for both decorators.

**Recurring pattern, now 5 instances this session: tests passing for the wrong reason.** The own-key sweep arm
*survived* its wrong-key mutation — sealing to the wrong directory left the original file in place, and the
decorator's legacy tolerance made the read-back succeed. Round-trip assertions are structurally blind to a
write that lands somewhere else; only asserting on the bytes at the expected path catches it. **Fixture
hostility, not fixture convenience.**

Residual: bulk-import temp files still plaintext → `DF-359-bulk-import`.

### Wave 5 — ops flips (yours)
RLS prod flip (largest security gain still deferred; reversible via `Rls:Enabled`) → ClamAV prod (real clamd) → GlitchTip hardening (**`ENABLE_OPEN_USER_REGISTRATION=false` is the sharp edge** on a reachable instance).

### Wave 6 — `DF-approveoffer-retire`
Needs a data-cleanup migration before the constant can be removed (`UpdateRoleValidator` rejects permissions absent from the catalog, and the back-fill never removes rows). Bundle with the next authorization change.

---
