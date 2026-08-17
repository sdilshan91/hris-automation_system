# Gap-Closure Queue — the loop's source of truth

> **This file is the loop's ledger.** One item per iteration, executed **top-down**. Mark `[~]` when a branch is
> cut, `[x]` when its PR merges. **Never work an item that is not the topmost `[ ]`** — that is what keeps the
> loop free of conflicts and duplicates. If reality changes, re-sort this file *before* starting the next item,
> and say why in the changelog.
>
> **Source:** [`Architecture/gap-analysis/REFRESH-2026-08-17.md`](../../Architecture/gap-analysis/REFRESH-2026-08-17.md) §7,
> plus the iteration-0 runtime probe (below). Base branch: `test/local-subdomains`.

---

## The bar — what "same level" means (decided 2026-08-17)

A module is **done** only when **all four legs** hold. This is the repo's own gap-analysis evidence bar, applied
uniformly rather than selectively:

| Leg | Requirement |
|---|---|
| **1 — code** | The capability exists in `src/`. |
| **2 — wired** | Reachable: handler dispatched, DI registered, route present **and** in nav — no orphans. |
| **3 — tested** | Test-bound, with **real-Postgres arms on stateful paths**. EF InMemory is acceptable only for pure logic. |
| **4 — contract** | The FE consumes **generated contract types**. No raw `http.get<IFoo>` assertions. |

**Chosen deliberately over cheaper bars.** 9 of 9 defects found in the refresh were leg 4; 6 of the FRAGILE
verdicts were leg 3. A bar that skips either leaves the defect class alive and merely clears today's instances.

## The S-1 method — decided 2026-08-17

**Generated types + explicit mappers.** Replace each hand-written response interface with `Schema<'…'>`. Where
the UI genuinely needs a different shape, write an explicit mapper whose **input** is the generated type.

```ts
type ResultsWire = Schema<'Feedback360ResultsDto'>;   // the contract, not a hand-written guess
function toView(w: ResultsWire): IResultsView { … }    // view-model, input type-checked
```

**Why this and not `map()` adapters** (which GAP-012's register row prescribes, and which PR #510 used): an
adapter still consumes a *hand-written* wire interface, so the unchecked assertion just moves one layer down and
CI still cannot see it. With generated types a renamed C# field becomes a **compile error**, not a silent
`undefined`. **Every one of the 9 defects becomes unwritable.**

> **Consequence for this queue:** a confirmed contract defect is fixed **by migrating that model file to
> generated types**, never by renaming a field. A rename would be the same work twice.

---

## Iteration 0 — runtime probe ✅ DONE 2026-08-17

**5 of 5 suspected defects CONFIRMED** against the running stack. Two are worse than the static read showed:

| # | Defect | How confirmed | Correction to the static read |
|---:|---|---|---|
| 1 | Employee profile save | **Runtime-reproduced.** `GET …/profile` → `rowVersion: 948`, **no `xmin` key**. Replaying the FE's body `{"rowVersion":null}` → **400** *"The JSON value could not be converted to System.UInt32"*. Correct token → **200**. | Fails as a **400 model-binding error**, not the predicted EF concurrency mismatch. Same fix. |
| 2 | LOP summary | **Runtime-reproduced.** Response `data` is an **object**; and the screen calls `getLopSummary()` with **no params** against an endpoint requiring `employeeId/from/to` → **400** before the cast runs. | **Two bugs, not one.** Fixing only the documented array-cast leaves the screen 400ing. |
| 3 | Recommendation workspace | Contract-confirmed: `page` is an **integer**, rows live at top-level `rows`. Live body unobservable (0 cycles seeded). | Silence confirmed by JS semantics — `(number).rows` is `undefined`, never throws. |
| 4 | Self-assessment | Contract-confirmed: API sends `items`/`isSelfAssessmentOpen`/`weightedSelfScore`/`submittedAt`. Live body unobservable (0 cycles seeded). | **It will THROW**, not render empty — `assessment()?.goals.length` on `undefined`. |
| 5 | Offboarding complete | Contract-confirmed: task `clearanceStatus` ∈ `approved`/`pending_issues`/`null`; `"cleared"` exists only at **department** level. | FE must mirror the BE gate (`OffboardingService.cs:340`), not just swap a string. |

**Two blockers the probe surfaced, both filed below as their own items:** the FE nginx has **no `/api` proxy**, so
browser-level QA cannot reach the API; and the seed data has **1 employee, 0 appraisal cycles, 0 offboarding
instances**, so probes 3–5 could not be observed end-to-end.

---

## The queue

### Tier A — cheap, high-leverage, unblocks others

- [x] **A1 · `/verify-fix BUG-068`** ✅ **DONE 2026-08-17** — 39/39 green on real Postgres; fix confirmed at `ApplicantConversionService.cs:168`. **The 9 TCs are UNBLOCKED, deliberately NOT flipped to pass** — `/test-us US-REC-010` still owes the verdicts. Five sibling findings on the same frozen block are marked *contradicted*, not fixed; each needs its own `/verify-fix` (added as A1b below). — ledger-only, no code. Six of nine live pessimistic contradictions are one
  frozen file (`TEST-STATUS.md:193-203`); **nine TCs sit BLOCKED behind a fix from six weeks earlier**. The
  regression test already exists. *Highest value per effort in the whole refresh.*
- [ ] **A2 · Production startup config** — `appsettings.json:21` ships `Rls:Enabled=true` with a blank
  `PrivilegedConnection`, and `DependencyInjection.cs:63-70` **throws on exactly that**. Only
  `appsettings.Development.json:9` saves dev. Add `PrivilegedConnection` to `PRODUCTION-CHECKLIST.md` beside the
  existing `Smtp:Host` gate (which currently has **zero** TLS/DNS lines either), and correct
  `BA/STATUS.md:200`'s stale "committed OFF".
- [ ] **A3 · Stale-comment sweep** — `RealNotificationDispatcher.cs:32` ("the 12 `LogOnly*` seams are NOT rewired")
  is **false**: one live log-only registration, and it is performance's documented deferral. That sentence is the
  sole source of the plan's P3 *"notification delivery rewire (biggest surface)"* epic. Also
  `TenantProvisioningService.cs:31-34` (which has kept the US-ADM-011 engine dormant for 5 weeks) and ~10 job
  headers. **Comment-only, and it retires an epic for work already done.**

- [ ] **A1b · `/verify-fix` the five siblings on the same frozen block** — BUG-055, BUG-058, BUG-003, ISSUE-122,
  ISSUE-140. All five are narrated live by `TEST-STATUS.md:193-203` and RESOLVED/PARTIAL in `TEST-FINDINGS.md`.
  **Ledger-vs-ledger is not evidence** — each needs a run against code before its row is flipped.
- [ ] **A1c · `/test-us US-REC-010`** — execute the 9 TCs A1 unblocked. They have never been run; they are not
  passing, they are merely no longer blocked.

### Tier B — confirmed live defects, each fixed via generated types

- [ ] **B1 · Employee profile save** *(core-hr — the primary screen, runtime-reproduced 400)*. Migrate
  `employee.models.ts` profile types to generated; fix `employee-profile.component.ts:2614`. Delete the
  `xmin: '12345'` mock in `employee-profile.component.spec.ts:112` and assert against the generated shape.
- [ ] **B2 · LOP summary** *(leave — two bugs)*. Send the required `employeeId/from/to`, **and** read
  `response.entries`. Migrate the LOP models to generated types.
- [ ] **B3 · Recommendation workspace + self-assessment** *(performance — worst-affected module, 102 interfaces)*.
  `ws.rows` not `ws.page.rows`; `items`/`isSelfAssessmentOpen`/`weightedSelfScore`/`submittedAt`. Rebuild
  `recommendation-workspace.component.spec.ts:53` and `self-assessment.service.spec.ts:32` from the generated types.
- [ ] **B4 · Offboarding complete** *(onboarding)*. Mirror the BE gate at `OffboardingService.cs:340`; correct the
  `IOffboardingTask.clearanceStatus` union to the real decision vocabulary.
- [ ] **B5 · Salary-grade Active toggle + payslip generation panel** — two silent no-ops sharing the same shape.
- [ ] **B6 · `GET /onboarding/templates/lookups`** — the FE calls it, no controller serves it, it is absent from the
  OpenAPI spec. Add the endpoint; replace the `createSpyObj` mock with an `HttpTestingController` arm.

### Tier C — activation gaps (the "cheapest large win", 0 of 6 closed in 35 commits)

- [ ] **C1 · GAP-029 workflow seed** — seed one 1-step manager-approval Leave definition at provisioning. Flips the
  **entire US-ADM-011 runtime engine** from dormant to live for every new tenant. **Needs one arm proving the
  seeded route approves identically** — it is a behaviour change for new tenants, not a pure fix.
- [ ] **C2 · GAP-027 dead download URL** — 5 consumers emit `/files/…`, which no route serves; plus the
  `downloadUrl`/`signedUrl` FE mismatch. Point them at the existing authenticated `/download` route.
- [ ] **C3 · GAP-025 audit pairing** — 3 unpaired call sites, and `employee_field_audit_logs` has **4 writers,
  0 readers**. Decide explicitly whether it gains a reader or is a forensic side-table; record in the vault.
- [ ] **C4 · GAP-026 terminated-employee enrollment** — add the `Status == Active` guard. *Carries an open
  decision: should an existing enrollment auto-terminate? Recommendation is no — AC-7 makes it manual.*
- [ ] **C5 · GAP-028 export bundle** — fix the emailed link first (**S**, a route already exists); documents ZIP +
  schema PDF are the M–L remainder.

### Tier D — the structural item (the only one whose cost grows)

- [ ] **D1 · S-1 migration**, module by module, worst-first. **669 hand-written interfaces vs 11 `Schema<>` uses.**
  Order by where it hurts: **performance (102) → admin-console (88) → leave (79) → payroll (75) → core-hr (71) →
  attendance (70) → recruitment (59) → auth (26)**.
  - *Cheapest wins:* attendance (drift is latent) and recruitment (adapters already exist).
  - *Do last:* authentication — 100% hand-written yet verified correct, so the risk there is future drift only.
  - Tier B items already migrate their own module's files; D1 finishes each module.

### Tier E — infrastructure + guards

- [ ] **E1 · GAP-033a security headers** — **zero** of the six §23.4 requires exist. `src/frontend/nginx.conf` is the
  **shipped production config** and sets only `Cache-Control`. Four cheap headers first; CSP separately
  (report-only → enforce) because of Angular Material inline styles.
- [ ] **E2 · `HRM.ArchitectureTests`** — ~6 NetArchTest rules. Mechanically catches the GAP-006 class forever.
  **Highest durable value per effort in the tail.**
- [ ] **E3 · Leg-3 parity** — port the InMemory integration suites on stateful paths to Testcontainers, worst-first:
  **reports (12 of 14 InMemory, on `GROUP BY`/ordering code) → leave (18 of 26) → admin-console provisioning/
  lifecycle** (subdomain uniqueness and owner FKs are exactly what InMemory ignores).
- [ ] **E4 · QA rig** — the FE nginx has no `/api` proxy (browser QA cannot reach the API), and seed data has
  1 employee / 0 cycles / 0 offboarding instances. **Both blocked full runtime verification in iteration 0.**
- [ ] **E5 · Nav orphans** — `/locations`, `/org-tree`, `/settings/custom-fields`, `/offboarding`,
  `/exit-interview/analytics` are routed with no nav entry or inbound link.

### Tier F — reclassified / decisions pending

- [ ] **F1 · GAP-019b Google sign-in** — **not billing, and not covered by the billing parking decision.**
  `EntraSsoService.cs:544` is generic OIDC and names itself the reuse path. *(Apple stays externally gated on the
  developer subscription.)*
- [ ] **F2 · GAP-020** — reword the register (rotation **is** possible via config + restart); the real gap is
  **no runtime rotation and no global token epoch**. Route to `/security-audit` for an exploitability rating
  **before** scheduling, as the plan already asked and nobody has done.
- [ ] **F3 · GAP-021 calibration** — `CyclePhase.CompletedOn` + a real `Performance.Calibrate` permission + the FE
  workspace. Note the downstream consumer: `RecommendationService.cs:470-479`'s BR-2 gate is a **proxy** that
  passes with zero calibrations applied.
- [ ] **F4 · GAP-030** — author the FR/BR/NFR of US-ADM-012 / US-PLT-004 **from the shipped code**; add matrix rows
  (three, not two — US-ADM-011 is also absent).

---

## Standing rules for the loop

1. **One item, one branch, one PR.** Never start item *n+1* before *n* merges. This is the whole conflict/duplicate defence.
2. **Never weaken a test to go green.** No `xit`/`fit`/`.skip`/`.only`/`[Fact(Skip)]`. Hooks enforce it.
3. **A contract fix migrates to generated types** — never a field rename. See the S-1 decision above.
4. **Every model-change PR regenerates the contract AND the TS types itself.** The GAP-S1 gate is **per-commit**.
5. **Mutation-verify the arm that matters.** A green test that stays green when you delete the code it guards is worthless — that has now happened twice in this repo, both caught only by mutation.
6. **Full gate before PR:** `dotnet build` → `dotnet test` → `npm run build` → `ng test` headless → `@integration-enforcer` + `@test-authenticator`.
7. **Out-of-lane findings are FLAGGED, never silently fixed** — filed to `TEST-FINDINGS.md` and folded into this queue, then the queue is re-sorted.
8. **Decisions go to the human** with recommended options, and the recommendation is the *best* option, not the cheapest.

## Changelog

- **2026-08-17** — Queue created from the gap-analysis refresh §7. Iteration 0 probe done: **5 of 5 confirmed**, two worse than the static read (LOP has a second primary bug; self-assessment throws rather than empties). Bar set at all four legs; S-1 method set to generated types + explicit mappers. #509/#510/#511 merged, so the queue starts from a clean trunk.
