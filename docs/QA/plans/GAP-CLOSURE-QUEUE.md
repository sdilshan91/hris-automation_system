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

## 🔁 AUTO-HEAL 2026-08-21 — the migration's own discoveries, folded in

> **Prime directive: never silently drop an out-of-lane discovery.** The D-migration slices surfaced ~25
> findings across three modules. They had been reported in commits and PR bodies but **not folded into this
> queue** — exactly the evaporation auto-heal exists to prevent. Filed now as [[BUG-306]], [[ISSUE-379]],
> [[ISSUE-380]], [[ISSUE-381]] and ranked below.
>
> **Grouped, not minted individually.** 25 separate IDs would bury the ledger; each finding carries a table of
> its instances. De-dup per the protocol.
>
> ### Re-sorted by severity × blast-radius × unblocks-others − gated
>
> | rank | item | why here |
> |---|---|---|
> | **1** | ~~P1 route breaks~~ ✅ **DONE** — 3 of BUG-306's 9 fixed | live breakage, no decision needed |
> | **2** | **E2 `HRM.ArchitectureTests`** | *unblocks-others*: a guard that prevents the whole GAP-006 class recurring. Value compounds over the 6 remaining modules, so it ranks above bigger items. |
> | **3** | **E1 security headers** | HIGH severity × systemic blast-radius (every session, every tenant). **Zero of six** §23.4 headers exist. S-sized, ungated. |
> | **4** | **C1 workflow seed** | *unblocks-others*: flips the entire US-ADM-011 runtime engine from dormant to live for every new tenant. S-sized. |
> | **5** | **B6 `/onboarding/templates/lookups`** | BUG-306 #9 — the only nonexistent route whose fix needs no decision (build the endpoint the FE already calls). |
> | **6** | **Remaining migration** (payroll → onboarding → admin → attendance → recruitment → auth) | preventive; each slice now fixes its own HIGH findings rather than deferring |
> | **7** | ISSUE-380 dead-code removals | LOW severity, local blast-radius, but trivially cheap |
> | **8** | ISSUE-381 codegen gap | MED; a hole in the gate itself, worth closing before the gate is relied on further |
>
> ### 🚧 Parked at the decision gate — NOT auto-scheduled
>
> Per the protocol these are **tracked and ranked, then wait for the human call**, regardless of raw score:
>
> | item | the decision |
> |---|---|
> | **ISSUE-379** (11 backend DTO gaps) | per feature: add the fields, or remove the UI that renders them |
> | **BUG-306 #4-#7** | per route: re-point the FE, or build the endpoint the FE assumes |
> | leave reports keyed-vs-positional | which side is authoritative — the backend's positional cells or the FE's keyed columns |
> | salary-grade `isActive` | honour it server-side, or remove the toggle |
>
> ### ⚠ Second pass — the first heal was itself incomplete
>
> The user asked whether the *earlier* session findings had been filed. They had not. The 2026-08-21 heal
> reconciled the recent migration slices and **recalled** the rest instead of enumerating it. Four more items
> were missing; now filed as [[BUG-307]] and [[ISSUE-382]], plus an owed [[ISSUE-232]] flip.
>
> **[[BUG-307]] came back higher than it was first flagged.** It was reported as a LOW "seed data" note; it is
> actually **tenant plan limits failing open** — a `plan_id` matching no `subscription_plans` row resolves
> `MaxEmployees` to unlimited, silently. The original rating described the *cause* (seed data) rather than the
> *effect* (a paid-plan cap that does not exist). Re-rated MED, possibly HIGH depending on how many real
> tenants carry an unmatched `plan_id` — **check that before scheduling.**
>
> **Process correction, recorded because it will recur:** a heal must **enumerate its sources** — every agent
> hand-back, every PR body, every OUT-OF-LANE block in the window — not recall them. Memory is exactly the
> mechanism the protocol exists to replace.
>
> **★ The one number worth carrying forward:** roughly **one field in five** of the hand-written interfaces
> describes an endpoint that was never built. These were not an accurate API description that drifted — they
> were written from what the UI wanted and never reconciled. The remaining ~570 interfaces should be expected
> to behave the same way, so "finish the migration" is not a mechanical task and should not be estimated as one.

## The queue

### Tier A — cheap, high-leverage, unblocks others

- [x] **A1 · `/verify-fix BUG-068`** ✅ **DONE 2026-08-17 (PR pending)** — 39/39 green on a real Postgres
  Testcontainer; fix confirmed at `ApplicantConversionService.cs:168`. **The 9 TCs are UNBLOCKED, deliberately
  NOT flipped to pass** — flipping without running is how ISSUE-371 and ISSUE-377 happened. The five sibling
  findings on the same frozen block are marked *contradicted*, not fixed. Spawned **A1b** and **A1c**.
- [x] **A1b · `/verify-fix` the five siblings** ✅ **DONE 2026-08-17 — 5 of 5 genuinely RESOLVED.** Each has the fix
  at a cited `file:line` + a **discriminating** regression test that passed. BUG-003 (CRIT) additionally
  **live-probed**: cross-tenant header spoof → **403 `cross_tenant_denied`**, plus real-Postgres RLS `42501`.
  **ISSUE-140 was understated** — recorded PARTIAL (FR-5 only), actually FR-5 **and** FR-8 **and** FR-9, all
  DI-wired and test-bound. *Fourth confirmed pessimistic-direction ledger error.*
  Folded into the A1 PR rather than its own branch: it edits the same lines A1's banner created, so a second
  branch would have guaranteed the conflict the one-item rule exists to prevent.
- [x] **A1c · `/test-us US-REC-010`** ✅ **DONE 2026-08-18 (#514) — 7 PASS · 1 FAIL · 1 BLOCKED.** BUG-068 confirmed
  dead in the flesh (every convert 201, never a 500). **Found [[BUG-305]]** — vacancy auto-close works but BR-5's
  recruiter + remaining-pipeline notifications were never built, next to a comment at
  `ApplicantConversionService.cs:478` claiming they were. **Three TCs were STALE, not failing** (authored against
  the pre-ISSUE-140 stub world) and were executed against real behaviour rather than forced to a verdict. `-13`
  BLOCKED honestly: the P95-over-100-conversions arm is not executable. Residue: none.
  - **Follow-on filed:** BUG-305 (Tier C candidate) · ISSUE-232 verified resolved by `-05`, ledger flip owed.
- [x] **A2 · Production startup config** ✅ **DONE 2026-08-18 — and the item was two-thirds wrong as written.**
  **The startup throw is not a defect** — `appsettings.json:9-12` states RLS is *"Enabled by DEFAULT so a
  deployment that forgets to configure it gets isolation ON"*, and the throw is the intended fail-closed posture.
  **And `PrivilegedConnection` was already in `PRODUCTION-CHECKLIST.md` in four places** (`:109 :119 :125 :166`),
  including `:125` spelling out the refuse-to-start behaviour verbatim. The prescription was written without
  re-reading the target — **the exact failure the refresh's own §8 names** ("verify prescriptions, not just
  findings"), committed in the report that named it.
  **What was genuinely missing, and is now fixed:** a **TLS/DNS section** (the checklist matched *zero* of
  TLS/HTTPS/certificate/DNS/reverse-proxy/load-balancer across 283 lines) covering wildcard DNS, DNS-01 wildcard
  issuance, SNI verified on a real tenant subdomain, TLS 1.2 floor, the `admin.*` dead-entry-point warning
  (GAP-038) and where the six missing security headers must live (GAP-033a); plus `BA/STATUS.md:200`'s stale
  **"committed OFF"**, corrected — the operational consequence is the *inverse* of what it implied.
- [ ] **A3 · Stale-comment sweep** — `RealNotificationDispatcher.cs:32` ("the 12 `LogOnly*` seams are NOT rewired")
  is **false**: one live log-only registration, and it is performance's documented deferral. That sentence is the
  sole source of the plan's P3 *"notification delivery rewire (biggest surface)"* epic. Also
  `TenantProvisioningService.cs:31-34` (which has kept the US-ADM-011 engine dormant for 5 weeks) and ~10 job
  headers. **Comment-only, and it retires an epic for work already done.**


> ## ⚠ RESTRUCTURED 2026-08-19 — Tier B folded into per-module slices
>
> **Tier B (per-defect) and Tier D (per-module migration) covered the same files.** B3 "fix the performance
> contract breaks" *is* D-performance; B2 was D-leave; B5 spanned D-core-hr and D-payroll. As originally
> written this queue would have opened and migrated those model files **twice** — the duplication the loop
> exists to avoid, baked into the plan.
>
> **Now one module-by-module pass, ordered so modules containing live user-facing defects go first.** Each
> module is opened once: its interfaces migrate to generated types *and* its live defects are fixed in the
> same slice.
>
> | order | slice | iface | carries |
> |---|---|---:|---|
> | 1 | **D-performance** | 102 | the silent-empty team list + self-assessment throwing (was B3) |
> | 2 | **D-leave** | 79 | deletes the now-dead FE `getLopSummary` + its 9 tests (B2 residue) |
> | 3 | **D-core-hr** | 71 | salary-grade Active no-op (was B5). B1 already did the profile slice |
> | 4 | **D-payroll** | 75 | payslip generation panel (was B5) |
> | 5 | **D-onboarding** | 43 | offboarding complete-gate (was B4) |
> | 6-8 | D-admin 88 · D-attendance 70 · D-recruitment 59 | | no live defects; recruitment cheapest (adapters exist) |
> | 9 | **D-auth** | 26 | **LAST** — 100% hand-written yet verified correct, so the risk is future drift only |
>
> **B6 stays separate**: a missing endpoint (`/onboarding/templates/lookups` 404s) is not contract drift, and
> no migration slice could invent it.
>
> **★ Two counts in that table are suspect and must be re-measured per slice, not trusted.** The 669 total
> came from a grep whose method already disagreed with the register's own figure (633) at the *same commit* —
> so these per-module numbers are one method's answer, not ground truth. Re-measure when each slice starts.

### Tier B — confirmed live defects, each fixed via generated types

- [x] **B1 · Employee profile save** ✅ **DONE (#519)** — migrated to generated types; mutation-verified (3 arms RED). Also the first slice of D-core-hr.
  - *(original text below)*
- [x] ~~B1 original~~ *(core-hr — the primary screen, runtime-reproduced 400)*. Migrate
  `employee.models.ts` profile types to generated; fix `employee-profile.component.ts:2614`. Delete the
  `xmin: '12345'` mock in `employee-profile.component.spec.ts:112` and assert against the generated shape.
- [x] **B2 · LOP register** ✅ **DONE (#520)** — was NOT 'two bugs': the screen is a cross-employee register calling a per-employee payroll endpoint, so it 400'd before the documented cast was reached. Built `GET /leaves/lop-register` + FE migration + **the nav entry that makes the screen reachable at all**. 5 surviving mutations closed. **Deferred:** the now-dead FE `getLopSummary` + its 9 tests → D-leave.
  - *(original text below)*
- [x] ~~B2 original~~ *(leave — two bugs)*. Send the required `employeeId/from/to`, **and** read
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

- [x] **C1 · GAP-029 workflow seed** — **DONE, PR #534 (merged 2026-08-21).** The equivalence arm this entry
  asked for exists and is mutation-verified (`LineManager` → `DepartmentHead` reddens it, and nothing else).
  **Two corrections to this entry's own framing:**
  1. *"at provisioning"* was too narrow — provisioning alone would have split tenants into two populations with
     different approval mechanics, told apart only by signup date. It now **backfills existing tenants too**,
     via the reconciler that already existed for the default shift.
  2. *"a behaviour change for new tenants"* understated it. Because the workflow path was dead code for
     **every** tenant, turning it on would have activated three dormant defects at once. C1 was **parked** and
     shipped only after **BUG-309** (a user id passed into an `approverEmployeeId` slot) and **BUG-310** (an
     unreachable line manager still created an instance nobody could approve — and the engine snapshots the
     approver, so unlike legacy it never self-healed) were fixed and merged in #532. **ISSUE-387** (no semantic
     `Leave.Approved`/`Rejected` audit rows on the workflow path) remains OPEN pending a compliance call.
  Also closed a tenant-scoping hole the arms could not kill: dropping the backfill's `TenantId` predicate
  passed every single-tenant arm. 7 arms, 3/3 mutations RED, gate 5470/5470.
  **Spawned:** ISSUE-386 (Offer + Overtime still fall through to legacy; each needs its own equivalence arm
  before seeding, because their legacy approver is *not* verified to be the line manager).
- [ ] **C2 · GAP-027 dead download URL** — 5 consumers emit `/files/…`, which no route serves; plus the
  `downloadUrl`/`signedUrl` FE mismatch. Point them at the existing authenticated `/download` route.
- [ ] **C3 · GAP-025 audit pairing** — 3 unpaired call sites, and `employee_field_audit_logs` has **4 writers,
  0 readers**. Decide explicitly whether it gains a reader or is a forensic side-table; record in the vault.
- [ ] **C4 · GAP-026 terminated-employee enrollment** — add the `Status == Active` guard.
  **DECIDED 2026-08-21: guard NEW enrollments only; existing enrollments are left untouched.** AC-7 makes
  termination manual, and a validation guard must not silently mutate live benefit/training records as a
  side effect of a deploy. The open decision this entry carried is now closed.
- [ ] **C5 · GAP-028 export bundle** — fix the emailed link first (**S**, a route already exists); documents ZIP +
  schema PDF are the M–L remainder.

### Tier D — the structural item (the only one whose cost grows)

> ### ★ RE-STRUCTURED 2026-08-21 — three decisions, and a measurement that forced them
>
> **The D1 metric was measuring the wrong thing.** Measured consistently at two points in git history
> (same grep, both commits): `Schema<>` uses went **10 → 109**, while hand-written interfaces went
> **695 → 692**. Ninety-nine call sites migrated moved the interface count by three. In core-hr — a
> "migrated" module — **68 of 71 interfaces are still live**. The migration has been typing the *wire calls*;
> the hand-written *view-model* interfaces stay.
>
> Splitting them shows why that is fine: **450** interfaces are referenced by a `.service.ts` (they describe
> wire payloads — real FE↔BE drift risk); **232** are never referenced by a service at all (pure view models
> that never cross the wire, and therefore *cannot* drift).
>
> **DECIDED — D1 targets the 450 wire-adjacent interfaces, plus a drift guard.** The 232 pure view-model ones
> are out of scope: converting them reduces no risk. A guard modelled on `PlanLimitLookupUsageGuardTests`
> blocks *new* hand-written wire interfaces — without it D1 never finishes, which is exactly what the
> 99-migrations-for-3-removed number demonstrates.
>
> **DECIDED — contract-complete before any further FE migration.** Every backend change that alters
> `api-types.ts` (the ISSUE-379 exposure adds, B6's missing endpoint, `/leaves/reports/summary`, C2's dead
> route) lands FIRST. Migrating a module against a contract that is about to change guarantees rework — the
> duplication this queue exists to avoid.
>
> **DECIDED — C4 guards new enrollments only** (below).
>
> **Phase order:**
> 1. **Ledger truth** — correct the rows that overstate (done: ISSUE-379 corrected, [[BUG-311]] filed).
> 2. **Contract completion** — ISSUE-379 exposure adds · B6 · `/reports/summary` · C2. One final regen.
> 3. **FE per module** against the settled contract — B3/B4/B5 first slice, D1 finishes each.
> 4. **Behaviour gaps** — C3 · C4 · C5 · E5.
> 5. **Infra, tests, docs** — E2 · E3 · E4 · F1–F4 · A3.
>
> **B3/B4/B5 are NOT duplicates of D1** — this queue already says "Tier B items already migrate their own
> module's files; D1 finishes each module." Checked before "fixing" a non-problem.

- [ ] **D1 · S-1 migration** — **RE-SCOPED (see above): the 450 wire-adjacent interfaces + a drift guard.**
  Original text: module by module, worst-first. **669 hand-written interfaces vs 11 `Schema<>` uses** —
  *both figures now stale; measured 692 and 109 on 2026-08-21.*
  Order by where it hurts: **performance (102) → admin-console (88) → leave (79) → payroll (75) → core-hr (71) →
  attendance (70) → recruitment (59) → auth (26)**.
  - *Cheapest wins:* attendance (drift is latent) and recruitment (adapters already exist).
  - *Do last:* authentication — 100% hand-written yet verified correct, so the risk there is future drift only.
  - Tier B items already migrate their own module's files; D1 finishes each module.

### Tier E — infrastructure + guards

- [x] **E1 · GAP-033a security headers** — **DONE, PR #530 (merged 2026-08-21).** All six now set in both places;
  CSP ships report-only on the SPA as planned (Angular Material injects runtime `<style>`).
  **The gap was bigger than the entry said.** Adding them at nginx's *server* level looked complete and was not:
  nginx inherits `add_header` only if the current level declares none, and the static-asset `location` block
  already declared `Cache-Control` — silently dropping all six for every `.js`/`.css`/`.woff2`/image.
  Measured, not argued: `/app.js` **0 of 6** before → **6 of 6** after, while `/` read 6 of 6 *both* times, so a
  browser spot-check would have hidden it. `NginxSecurityHeaderInheritanceTests` now fails if any future
  `location` block repeats the mistake. 3/3 mutations RED. Gate 5444/5444 + 4104/4104.
  **Spawned:** BUG-308 (HSTS dead behind the TLS proxy — in flight), ISSUE-383 (Swagger ordering).
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
