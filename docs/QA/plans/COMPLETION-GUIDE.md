# Completion Guide — how to close what the 2026-09-01 audit found

> **Role of this file, and what it is NOT.** Three documents, three questions, no overlap:
>
> | File | Answers |
> |---|---|
> | [`COMPLETION-PLAN.md`](COMPLETION-PLAN.md) | **What** is outstanding, and **why** it matters |
> | [`GAP-CLOSURE-QUEUE.md`](GAP-CLOSURE-QUEUE.md) | **In what order** — the loop executes it top-down, one item per iteration |
> | **this file** | **How** to close each item: the fix, the proof, the trap |
>
> It is a recipe book, **not a backlog**. It holds no priority order and no status — those live in the
> queue. If you find yourself ticking items here, stop: you are recreating the five-competing-queues
> problem this set of files was just rebuilt to eliminate.
>
> **Everything below is a *static* verdict.** The audit read code; it ran nothing. Every FE↔BE claim
> is falsifiable in about a minute against a running stack — do that first, it is cheaper than the fix.

---

## 0. Read this before touching anything

**This codebase's defect class is wiring, not capability.** Of 18 PARTIAL gaps, every one failed
leg 2 (wired/reachable) or leg 3 (test-bound). **Not one failed leg 1.** The backend almost always
exists. Before writing a line, check whether the thing you are about to build is already there and
merely unreachable.

**A green suite is not evidence here.** Three live user-facing breaks shipped behind passing tests
because the specs assert the broken shape. If you fix code without fixing its spec, you have removed
the symptom and kept the blind spot.

**Verify before you build (the 29% rule).** 11 of 38 findings marked live were already fixed. Re-read
the code before starting any item; the ledger is a claim.

**Never weaken a test to go green.** Not a style rule — `test-integrity-guard` denies the write.

---

## 1. Bug fixing — live defects, in fix order

### 1.1 `G1` · Part-time overtime is paid on a full-time base — **money**

- **Where** `src/backend/HRM.Infrastructure/Services/PayrollRunProcessor.cs:977-978`
- **What** `PayrollOvertimeCalculator.Compute` takes **7** parameters
  (`…, defaultMultiplier, fte, fteScaledBase`). The call passes **4**, so `fte` defaults to `1.0`
  and `fteScaledBase` to `false` — always. `AttendanceSettings.FteScaledOvertimeBase` is persisted
  (`AttendanceSettings.cs:184`), mapped, settable, and **inert**.
- **Fix** Thread `Employee.Fte` and the tenant flag through the private wrapper `ComputeOvertime`
  (`:968-969`, currently takes no employee) and the call site at `:518`.
- **Prove it** An **integration** arm, not a unit test: `OvertimeFteBaseTests.cs:10-13` already
  concedes it "proves the MATH, not the plumbing" — that is exactly the hole. Assert a 0.5-FTE
  employee's OT hourly base is half the full-time one, end-to-end through `PayrollRunProcessor`.
- **Trap** Do not "fix" it by changing the calculator's defaults. The defaults are correct; the
  call site is wrong.

### 1.2 `G2` · Blank `Jwt:PrivateKey` in Production yields an ephemeral signing key

- **Where** `HRM.Api/Program.cs:151-162`, `JwtService.cs:41-47`
- **What** `JwtKeyRingOptions` has no `IValidateOptions`/`ValidateOnStart`, so a blank key silently
  generates a per-process RSA key. Every restart invalidates all tokens; multi-instance deployments
  reject each other's.
- **Fix** Validate on start and fail loudly — the pattern queue item `A2` already established for the
  other required secrets.
- **Prove it** A DI test that builds the provider with a blank key under `Production` and asserts the
  throw. **Note the shape of `GAP-015`'s failure while you are here** (§2.7): its Production guard has
  no test because the fixture never sets an environment name. Do not repeat that.
- **Trap** `TokenValidationParameters` is snapshotted once at `Program.cs:162`; there is no
  `IOptionsMonitor`. Validation must happen at startup or it happens never.

### 1.3 `G3` · Three live breaks whose specs mock the broken shape — **one branch, six edits**

Fix the code **and** its spec in the same PR for each. Fixing only the code leaves the detector broken.

| | Where | What | Fix |
|---|---|---|---|
| a | `careers-page.component.ts:141` vs `CareersController.cs:53` | List links `['/careers', v.id]` (a GUID); the route matches `{slug}` on `x.Slug`. A GUID never equals a slug, so **every vacancy click 404s** and no visitor reaches the apply form. | Link `v.slug`. `applicant.models.ts:81` already documents the requirement. **Spec:** `vacancy-detail.component.spec.ts:77` feeds `'vac-1'` — make it assert a real slug from the list payload. |
| b | `performance-goal.service.ts:53-56` vs `GoalDtos.cs:62-66` | BE returns `{cycleId, members}`; service types it `ITeamGoalStatus[] \| {data:[…]}`, so `toArray()` yields `[]` and the manager dashboard renders **"No team members yet" on every load**. US-PRF-001 AC-4 dead. | Map `.members`. **Spec:** `performance-goal.service.spec.ts:135` flushes `{data: …}` — a shape the endpoint never returns. |
| c | `onboarding-checklist.service.ts:68`; `onboarding-template.service.ts:94,103` | `/checklists/preview` exists on no controller; activate/deactivate use `PATCH` against `[HttpPost]` (`OnboardingTemplatesController.cs:155,174`) → **405**. All reachable from routed components. | Build the preview endpoint or delete the control; switch the verbs. **Specs:** both currently assert the wrong verb/route. |

- **Prove it** One Playwright arm per surface. All three are reachable from routed components, so an
  e2e click is the honest proof — this is precisely the class of break unit specs proved unable to catch.

### 1.4 Contract breaks — MED, mechanical

| Item | Where | Fix |
|---|---|---|
| `G8` | `dashboard.models.ts:336,369,404,406`; `review-signoff.models.ts:376,387,389` | **The wire already sends these.** `availableExportFormats`, `ratingScaleMax`/`finalScore`/`cycleName`, `scoreScaleMax`/`cycleLabel` all ship; the mappers hardcode `[]`/`0`/`''`/`null` **under comments asserting the wire lacks them**. ~10 lines. **Correct the comments in the same PR — they are how the ledger and code drifted apart.** Cheapest item in the guide. |
| `ISSUE-372` | `payroll.service.ts:117,134` | `/salary-components/reorder` and `/validate-formula` are live 404s. Build the routes or remove the drag-reorder and Test-formula controls. Do not leave dead affordances. |
| `ISSUE-374` | `onboarding-checklist.service.ts:108` | FE sends `{tasks}`, BE expects `{AddTasks, TaskChanges}` — edits are **silently dropped**. |
| `ISSUE-381` | `LeaveEntitlementsController.cs:279-283` | Codegen emits `content?: never` because one action returns JSON-or-file. **Split the routes**; the typed annotation alone did not fix it. |
| `ISSUE-382#1` | `bulk-import.models.ts:68-77` | `isImportResult()` tests `'total' in resp`, but the wire **always** sends `total`, so the async branch is unreachable and >500-row imports never poll. |

---

## 2. Gaps — closing them, by what leg fails

### 2.1 `G4` · `GAP-036` — no DSAR / right-to-erasure path — **compliance**
`IAuditAnonymizationService` is DI-registered (`DependencyInjection.cs:781`) and **called by nothing**
outside tests. `DataExportController` is tenant-wide bulk export, not subject access.
**This needs a story before it needs code** — see §4. Today a data-subject request can only be served
by manual DB intervention.

### 2.2 `G5` · `GAP-001` — tenant isolation is held by convention
Layers 1/2/4 of the off-switch are fail-open by construction: `TenantResolutionMiddleware.cs:90-93`
passes through, the `AppDbContext.cs:272-289` filters read `!IsResolved || …` (a tautology),
`TenantAccessGuardMiddleware.cs:40` skips. Layer 3's fix is inert in shipped config.
Nothing leaks today **only** because ~494 hand-written `!IsResolved` guards across 104 service files
fail-close first. That is a convention holding Critical Rule #1.
- **Cheapest real mitigation:** an HTTP-level negative test in `HRM.Tests/Integration/Http/` — an
  unresolved-tenant request to `/api/v1/tenant/*` must not return data. It is what catches the **first
  omitted guard** on a new read path.
- **Do not** attempt to "fix the layers" as a side quest; that is an architecture change, not a task.

### 2.3 `G6` · `GAP-005` — audit append-only is a runbook checkbox
`roles.sql:70-71` has the REVOKE, but **nothing runs `roles.sql`** — not the app, EF, `ops/`,
`scripts/` or CI; the file says so itself at `:3-4`. Worse, `RlsIsolationPostgresTests.cs:431`
**hand-mirrors** the revoke in its fixture, so it proves the intended privilege set, not that
`roles.sql` produces it.
**Pick one:** wire the file into a documented apply step, or correct `AuditLogController.cs:21-23`,
which claims append-only is "ENFORCED rather than merely conventional". Leaving both is the worst option.

### 2.4 `G7` · `GAP-007` — 354 `IgnoreQueryFilters()` sites, none machine-reviewable
Measured 354 in non-test `src/backend` (the register said 270) plus 472 in tests. **Zero** carry
`// nosemgrep`. `.semgrep/tenant-isolation.yml:55` is `WARNING` and its CI step is `continue-on-error`.
- **Campaign-shaped** — run `/campaign`, and heed its Phase-1 survey: >20% non-mechanical stops it.
- The written rationale is "RLS backstops them". **That does not hold in dev/CI, where
  `Rls:Enabled=false`.** Decide whether the rationale survives before annotating 354 sites on it.

### 2.5 `G14` · `GAP-024` — sweep jobs log no tenant
`TenantJobRunner.cs:31-40` sets `ITenantContext` but never `LogContext.PushProperty`, so the ~19
multi-tenant sweep jobs emit no `tenant_id`. `JobLogContextFilterTests.cs:72-77` pins this as intended,
conflating "cross-tenant" with "iterates every tenant". Isolation forensics are blind on the
**higher-risk** job class. Push the property inside the per-tenant loop and correct that test's premise.

### 2.6 `GAP-018` — rate limiting is per-instance
Key function and global limiter are correct; the store is in-process
(`Program.cs:554-555` documents the deferral). Effective ceiling = 300/min **× instance count**.
Move to the Redis fixed-window already used for portal magic links **when multi-instance ships** —
not before. No behavioural 429 test exists because the integration host sets `RateLimiting:Disabled=true`.

### 2.7 `G15` · `GAP-015` — the Production email guard has no test
`DependencyInjection.cs:868-876` throws on a blank `Smtp:Host` under Production. But
`EmailSenderDiRegistrationTests` never sets an environment name, so **the Production branch is never
entered** and a regression dropping the throw is caught by nothing. Also note the guard is
environment-gated, not behaviour-gated: Staging still delivers no password-reset or lockout mail.

### 2.8 Remaining PARTIALs — smaller, self-contained
`GAP-021` calibration has BE + tests but no FE, no `CyclePhase.CompletedOn`, no `Performance.Calibrate`
permission · `GAP-031` coverage is measured in CI but never gated (deliberate — look at the number
first) · `GAP-035` per-tenant sender exists **only** on payslips; password reset and lockout, which
BR-1 designates non-suppressible, still use the global From · `GAP-032` `PastDue` is an enum state no
code can reach · `GAP-038` the reserved `admin` host is a dead login (use `platform`); docs and FE
route comments still say `admin.yourhrm.com` · `GAP-040` tech-doc §10 documents folders that do not exist.

---

## 3. TODO items — corrections, not code

These close backlog without writing a feature. **Do them first: they are the cheapest wins in the guide.**

### 3.1 `G9` · `/verify-fix` the 11 findings that are already fixed
`BUG-003` · `BUG-056` · `BUG-298` · `BUG-301` · `BUG-307` · `ISSUE-021` · `ISSUE-232` · `ISSUE-280` ·
`ISSUE-362` · `ISSUE-364` · `BUG-003 (ATT-004 ext)`. Each carries `file:line` proof in
[`COMPLETION-PLAN.md`](COMPLETION-PLAN.md).
- **This is 29% of the "live" backlog evaporating without a code change.**
- `ISSUE-364`'s header says `OPEN` while its own body says `RESOLVED 2026-08-10`; `BUG-307`'s entry
  still claims nine sites remain when all ten were migrated (#536/#539/#540).
- **Route through `/verify-fix`. Do not hand-edit a status** — that is the only skill authorised to
  close a finding, and it re-runs the TCs rather than trusting the audit.

### 3.2 `G10` · `/test-us US-PRF-005`
`ISSUE-377` is **QA-execution debt, not code**: the 360-release endpoint shipped
(`Feedback360Controller.cs:249-266` + entity + migration). `TC-PRF-005-04/05/14` are still
`status: draft`, so three high/critical TCs have no live verdict.
**Do not close it by editing TC frontmatter.**

### 3.3 `G11` · Correct four wrong rows in the GAP register
- `GAP-006` — only **3 of 6** named entities were real holes; the other three are deliberately
  allow-listed with RLS policies (`TenantQueryFilterCoverageTests.cs:67-79`). **The register's
  "add 6 lines" prescription would have caused a regression.**
- `GAP-030` — "zero test cases" is false; both stories are test-covered. Only the BA docs are stubs.
- `GAP-034` — "assertions exist but never execute" understates it: there are **zero** axe assertions.
- `GAP-020` — headline premise false; config rotation exists and is tested. Reword to
  "no **online** lever" rather than closing it.

### 3.4 `G12` and stale in-code comments
Queue item `E2` references `HRM.ArchitectureTests`, **a project that does not exist**. Also correct:
`AuditLogController.cs:21-23` (§2.3), the performance FE mapper comments (§1.4 `G8`),
`DbInitializer.cs:446` (cites a guard that does not exist), `SalaryStructureIntegrationTests.cs:263`
(cites `ISSUE-108`, renumbered to `ISSUE-366`), and `ISSUE-375`'s location field (the false claim is at
tech-doc `:566` **and** `:2605` — amending one leaves the other).

---

## 4. Decision gate — do not schedule until decided

| Item | The decision |
|---|---|
| `GAP-019` | **11 platform capabilities are absent** — revenue/MRR, billing ops, platform-staff management, maintenance mode, broadcasts, growth/churn, platform report definitions + scheduling, Google and Apple sign-in. **No story exists for any of them.** Author them or record the deferral. |
| `GAP-036` | DSAR needs a story before build — intake workflow, scope, retention interaction. |
| `GAP-037` | Outbound webhooks: documented Phase-2 deferral, but tech-doc `:639`'s NFR table reads as if built. |
| `GAP-020` | No **online** rotation lever and no global revocation cutoff; containment needs a rolling restart. |
| `ISSUE-365` | Pick one: add the `/api` proxy to `nginx.conf`, **or** stop publishing `4200:80`. |
| `ISSUE-289`, `ISSUE-301`, `ISSUE-400`, `ISSUE-380` items 2–4 | Product calls, not build work. |

---

## 5. Test-health — `G13`

Two pieces of confirmed theater:
- `PlanModulesEntitlementTests.cs:118-126` assigns `seeded = PlanModules.All` then asserts it **equals
  `PlanModules.All`**. It would not catch the seed being repointed. `DbInitializer.cs:446` cites a guard
  named `PlanModulesSeedDriftTests` that does not exist.
- `ISSUE-286`'s fix (bulk-import `LocationId`) has only the **negative** arm; dropping `LocationId`
  would leave the suite green.

Also: `Feedback360ReleaseConcurrencyPostgresTests.cs:272` seeds `ReleasedByEmployeeId = Guid.Empty`,
baking the `ISSUE-382#2` defect value into the fixture — making that field nullable later will read as
a test break rather than a fix. Fix the fixture in the same PR.

Run `@test-authenticator` over anything you touch here.

---

## 6. Working method

**Sequence.** §3 corrections first (cheapest, and they shrink the backlog before you plan against it) →
§1.1/§1.2 (money, then auth) → §1.3 (user-facing) → §2 (gaps) → §4 only after the decision lands.

**Parallelise by disjoint write scope, never by dependency.** `G1` (payroll BE), `G3a` (careers FE),
`G3c` (onboarding FE) touch nothing in common and can run concurrently in worktrees. `G8`, `G3b` and
`ISSUE-379` all touch the performance mappers — **one lane, or they will conflict.**

**Per-item definition of done.** Code + wired/reachable + a test that fails without the fix + the
finding routed through `/verify-fix`. A fix with no failing-first test is a fix you cannot defend.

**Standing traps.**
- `ISSUE-365` — do **not** implement the shared `storageState` the finding proposes;
  `playwright.config.ts:15` forbids it (2026-08-11 decision).
- `ISSUE-377` — do **not** flip TC frontmatter; run the TCs.
- `GAP-006` — do **not** add the 6 query filters; 3 of them would regress.
- Never raw `dotnet test` — `bash scripts/run-backend-tests.sh` (ISSUE-312).
