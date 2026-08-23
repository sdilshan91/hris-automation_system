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
- [x] **B3 · Recommendation workspace + self-assessment** ✅ **ALREADY DONE — no PR, no work needed.** Verified
  against the code on 2026-08-22 before starting: both defects were fixed by the D-perf slices (#524/#525) and
  BUG-311 (#545). `mapRecommendationWorkspace` reads `w.rows` (not `ws.page.rows`); `mapSelfAssessment` reads
  `items`/`isSelfAssessmentOpen`/`weightedSelfScore`/`submittedAt`; both specs are rebuilt from `Schema<>`
  fixtures, and the performance module has **0 raw-typed `http.get<IFoo>` calls**.
  **Ticked without opening a PR because there was nothing to change.** Building the "fix" would have been the
  effort duplication this queue exists to prevent — the entry was stale, not wrong when written. *Third stale
  entry found by checking the code before trusting the ledger.*
- [x] **B4 · Offboarding complete** ✅ **DONE (#555)** — and the prescription was the wrong shape.
  **"Mirror the BE gate" would have re-created the defect.** The bug was that the rule existed twice — enforced
  in `OffboardingService`, *predicted* in the Angular `pendingMandatoryTitles()` — with nothing checking they
  agreed. Mirroring it more carefully leaves two descriptions to drift again. The rule now lives once, in
  `OffboardingCompletionGate`, and the instance DTO **projects** it (`canComplete` + `pendingMandatoryItems`)
  from the same call the endpoint enforces with; the client renders the answer.
  **The union correction was right, and bigger than stated.** One type was doing the work of two vocabularies:
  `'cleared'|'issues'|'pending'` is the DEPARTMENT traffic light, while a task carries
  `'approved'|'pending_issues'|null`. They share no member, so the gate's comparison could never be true —
  **every mandatory task blocked forever and the Complete button never enabled.**
  **Three more live defects surfaced in the same slice:** the reason dropdown POSTed the display string
  `'Contract End'`, which `Enum.TryParse` (strips `_`, not spaces) rejects → every use 400'd; `complete()` was
  typed against the wrong DTO, so a *successful* completion blanked the dashboard; and `parseCompleteBlocked`
  read an invented 409 shape, so AC-5's "which items block" never rendered.
  **Mutation testing paid twice** — two gate clauses were unkillable by the existing arms (one masked by a
  service invariant, one by the global soft-delete query filter). Both now pinned; the second needed a direct
  test of the gate as a pure function.
  8 mutations applied, 8 killed. Gate 5520/5520 + 4139/4139, build clean, contract OK.
- [x] **B5 · Salary-grade Active toggle + payslip generation panel** ✅ **DONE (#557)** — the "same shape" framing
  was right, and both were worse than one no-op each.
  **Decided with the human** (best option, not cheapest): honour `isActive` server-side rather than delete the
  toggle; mirror the server's generate states exactly with a confirm on the destructive case rather than a
  blanket ban.
  **The toggle:** the update DTO had no `IsActive`, so the switch was a save-time no-op — and the previous slice
  had **edited the spec to stop asserting the field** rather than fix it, recording the bug as intended
  behaviour. `DELETE` was the only writer and nothing set it back, so **a grade deactivated by mistake was
  stuck for the life of the tenant**; update now writes it, which is what makes reactivation possible at all.
  The wire field is **nullable** so a caller omitting it cannot silently reactivate.
  **Deactivation warns, never blocks** — job titles must resolve to an ACTIVE grade, so the DTO now carries
  `referencingJobTitleCount` (one grouped query) and *both* routes warn, including the list dialog that already
  had the count and was discarding it.
  **The payslip gate was wrong in BOTH directions:** enabled on `AwaitingApproval` (always 400s) and disabled
  on `Finalized` (which the backend explicitly supports). The rule is now written once; the backend's own
  duplicate literal (generate + retry) was extracted too.
  **The audit caught two of my own errors:** I regenerated the contract *before* making `IsActive` nullable, so
  the committed spec contradicted the design's central semantic and would have failed the CI gate; and my
  comment claimed the form omits `isActive` on create when the toggle was still rendered there — the same bug,
  one mode over. Toggle is now edit-only.
  **Mutation testing found two unguarded fields**, one of them the very field B5 exists for (a write mapper
  hard-coding `isActive: true` killed nothing, because every fixture used `true` and all form specs stub the
  service). 11 mutations, 11 killed. Gate 5525/5525 + 4163/4163.
- [x] **B6 · `GET /onboarding/templates/lookups`** ✅ **DONE (#550)** — endpoint built, contract regenerated, the
  `createSpyObj` mock replaced with an `HttpTestingController` arm.
  **The entry undersold the blast radius.** This was not one 404 on one screen: the template builder's *entire*
  reference vocabulary — task categories, owner roles, document types — came from this call, so every dropdown in
  it was permanently empty. A screen that renders with empty dropdowns looks like "no data configured yet", which
  is why a 404 survived this long without a bug report.
  **Sourced from the enums the backend already validates against, not hand-listed.** A hand-written lookup list is
  a second description of the same truth (S-1) and would drift the first time a category was added — the exact
  defect class this queue exists to close.

> ### 🔁 AUTO-HEAL 2026-08-23 — what the B4 auditors surfaced
>
> Filed per rule #7 rather than fixed in-slice. The two CRIT/HIGH items **were** in-lane and are fixed in #555
> (`complete()` DTO, the invented 409 shape); the rest are ranked here.
>
> | item | sev | where | why it waits |
> |---|---|---|---|
> | **Service specs never exercise the `ApiResponse` envelope** | MED | every `*.service.spec.ts` | Repo-wide convention: TestBeds register `provideHttpClient()` without `apiEnvelopeInterceptor` and flush pre-unwrapped bodies. **This is how the `complete()` mismatch stayed invisible.** Needs a decision: register the interceptor in service TestBeds, or accept that envelope coverage lives solely in `api-envelope.interceptor.spec.ts`. |
> | **16 barrel `TS2308` collisions** | LOW | `features/*/index.ts` | `todayIso` exported by two modules in onboarding; same class in leave/payroll/performance. Campaign-shaped, not a B4 fix. |
> | **`offboarding/initiate/:employeeId` has no inbound link** | LOW | `app.routes.ts:632` | Reachable only by typing the URL. Belongs with **E5 nav orphans** — added to that entry's list. |
>
> **The pattern worth carrying forward, in the auditor's words:** *the backend arms assert against real state
> and kill mutants; the frontend arms assert against fixtures the frontend itself invented.* B4 cured that in
> the completion **rule** and left it untreated in the completion **response** until the audit caught it. When a
> module's FE specs are migrated to `Schema<>`, check the **controller's `ProducesResponseType`** for each
> method — pointing the right-looking-but-wrong schema at an endpoint compiles fine.

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
- [x] **C2 · GAP-027 dead download URL** ✅ **DONE (#552 documents · #553 photos + onboarding attachment).**
  **The prescription was wrong: there was no existing `/download` route to point at.** `FileStorageService` writes
  to disk and hands back a storage key; `/files/…` was fabricated by string concatenation at five call sites and
  served by nothing. Every one of the five was a 404, not a mismatch — so "re-point the FE" would have re-pointed
  it at another dead URL. *Sixth confirmed case of a prescription written without re-reading the target.*
  **Profile photos were doubly broken** — the value STORED (`/{tenantId}/{path}`) and the value RETURNED
  (`/files/{tenantId}/{path}`) disagreed with each other, and neither was routable.
  **`<img src>` could not carry the fix.** In this app the access token IS a Bearer header (only the refresh token
  is a cookie), so an image URL cannot authenticate. The bytes are fetched through `HttpClient` and bound as an
  object URL — via **one directive**, because a missed `revokeObjectURL` leaks a blob per rendered avatar and
  three hand-written copies of that lifecycle is how one ends up wrong (S-1 again).
  **One of the five got NO endpoint, deliberately.** Nothing renders the asset-acknowledgment `ackUrl`, so the
  fabricated URL was deleted rather than a download built for a consumer that does not exist. A dead field holding
  a *plausible* URL invites someone to render it and inherit the 404.
  **The upload test could not fail** — `Contain("profile")` was satisfied by the dead
  `/files/…/profile/photo.jpg` itself. It now asserts equality with the real endpoint.
  Mutation-verified (removing the revoke reddens the destroy arm). Gate 5504/5504 + 4119/4119.
- [x] **C3 · GAP-025 audit pairing** ✅ **DONE (#559)** — decision recorded, and the entry's own site list was wrong.
  **DECIDED (human):** `employee_field_audit_logs` stays a **forensic side-table, write-only by design** — its
  snapshots carry masked PII that must not surface in a viewer everyone with audit access can read. ADR:
  [[2026-08-23-employee-field-audit-is-forensic]].
  **The obligation that decision creates.** A 4-writer/0-reader table is not dangerous for lacking a screen; it
  is dangerous because **nothing notices when a write stops**. Answering "forensic" without addressing that
  would have left it as fragile as before, with a decision recorded to make it look considered.
  **"3 unpaired sites" — right count, wrong membership.** ReportingStructure and the immediate status change
  were *already* paired (BUG-023, ISSUE-025). The real three were `UpdateProfileAsync`,
  `ApplyPendingFutureDatedChangesAsync`, and `CreateAsync` — which wrote **no audit anywhere**. Editing an
  employee left nothing in the viewer while merely **viewing** one logged `Employee.ProfileViewed`.
  **The audits made it bigger, twice.** Wiring: C3 was closing the register's LIST, not the CLASS —
  `OffboardingService.CompleteAsync` terminates an employee with no audit row of any kind, the most
  compliance-sensitive employee event being the least traceable. Tests: the change **could have shipped as a
  no-op** — `audit_logs` has no read-scoping global filter, every viewer read scopes `TenantId == tenantId`
  explicitly, and all three arms passed with a null tenant while two waived the property via
  `IgnoreQueryFilters()`. One arm now asserts through `AuditLogService.ListAsync`.
  **The guard took four versions to become falsifiable** (brace-splitter broke on interpolated strings →
  vacuous; `"AuditLogs.Add"` is a *substring* of `"EmployeeFieldAuditLogs.Add"` → unfalsifiable; helper
  inference from a bare name mention → blind on the one site it protects). 11 mutations, 11 killed.
  **Spawned:** ISSUE-392 (six `IAuditExempt` entities whose "own writer" has zero audit references, four
  money-related — **decision-gated**) · ISSUE-393 (null-tenant invisibility is platform-wide, ~30 writers) ·
  ISSUE-394 · ISSUE-395.
- [x] **C4 · GAP-026 terminated-employee enrollment** ✅ **DONE (#561)** — and the prescription would have
  caused a regression.
  **`Status == Active` was the WRONG guard.** It would have blocked **probationary and suspended** employees,
  who are employed and routinely enrol. Used the predicate this repo already had —
  `Terminated or Inactive` — which existed verbatim in **three** places (AttendanceService ×2, OvertimeService)
  and agreed, which is exactly what made a fourth copy look harmless. GAP-026 is what happened when the fourth
  site simply *forgot*. Now one definition (`EmployeeStatusExtensions`), three copies migrated, zero raw
  comparisons left.
  **Closed the CLASS, not the instance.** `TrainingService.EnrollAsync` had the identical gap one service over
  — course status checked, employee status never. The register names instances; closing only what it names is
  how the defect returns. (The C3 lesson, applied rather than re-learned.)
  **The 2026-08-21 decision held:** new enrollments only, existing ones untouched, with an arm proving a
  mid-year termination does not retroactively end cover. Eligibility LISTING is guarded too — showing plans the
  endpoint would refuse is the same defect one screen later, and it is how HR ends up believing a terminated
  employee is still covered.
  4/4 mutations killed on real Postgres, **including the over-block mutation** (`!= Active`, i.e. the
  register's own prescription) — so the arm that stops the fix over-reaching is demonstrably load-bearing
  rather than assumed. Gate 5543/5543.
- [ ] **C5 · GAP-028 export bundle** — fix the emailed link first (**S**, a route already exists); documents ZIP +
  schema PDF are the M–L remainder.

> ### 🔁 AUTO-HEAL 2026-08-23 (B5) — what its audit surfaced
>
> | item | sev | why it waits |
> |---|---|---|
> | **FE specs never exercise the `ApiResponse` envelope** | MED | Raised again by B5's audit after B4's. Service TestBeds register `provideHttpClient()` without `apiEnvelopeInterceptor` and flush pre-unwrapped bodies — the mechanism by which B4's `complete()` mismatch stayed invisible. **Still at the decision gate**; it is a repo-wide convention change. |
> | **`mapSalaryGrade` leaves 5 more fields unpinned** | LOW | `name`/`minAmount`/`midAmount`/`maxAmount`/`currency` can each be hard-coded without failing a test. B5 pinned the two that mattered (`isActive`, the count); the rest are a broader mapper-coverage sweep, not a B5 fix. |
>
> **The lesson B5 adds to the S-1 file:** a *cast* and a *duplicate literal* are the same defect wearing
> different clothes. The payslip gate had the rule three times — twice as a C# literal, once as a different
> and wrong TypeScript expression — and the two C# copies agreeing is what made it look fine.

> ### 🔁 AUTO-HEAL 2026-08-23 (C3) — the audit-marker class
>
> | item | sev | why it waits |
> |---|---|---|
> | **ISSUE-392 — six `IAuditExempt` entities with no writer** | HIGH | The marker permits exemption only if the entity's own service writes an explicit row. Six claim that and don't: two tenant-wide **money policies**, **overtime approve/reject**, **F&F settlement amounts**, plus two attachment paths. *Decision-gated:* add the writers, or drop the marker and accept interceptor capture — the latter changes audit volume, which is an ops call. **"Who approved this overtime?" is unanswerable today.** |
> | **ISSUE-393 — null-tenant audit rows are invisible** | MED | Platform-wide across ~30 writers. The model filter admits `TenantId == null`; every viewer read scopes explicitly. A row written with a null tenant is in the table and unreachable, with no detector. Wants a `SaveChanges` guard, not another test. |
> | ISSUE-394 · ISSUE-395 | LOW/MED | Audit addressability on bulk import + applicant conversion; offboarding's missing `EmploymentHistory` row. |
>
> **What C3 adds to the S-1 file:** the systemic defect here was not a duplicated *description* but a duplicated
> *promise* — `IAuditExempt` is a marker whose meaning ("something else audits this") nothing verifies. That is
> the same shape as a hand-written interface claiming to match a wire contract. A marker that asserts a fact
> about code elsewhere needs a guard, or it is a comment with a compiler-checked name.

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
> 1. **Ledger truth** ✅ — corrected the rows that overstate (ISSUE-379 corrected, [[BUG-311]] filed + fixed #545).
> 2. **Contract completion** ✅ **COMPLETE 2026-08-22** — ISSUE-379 exposure adds (#546-#549) · B6 (#550) ·
>    `/reports/summary` (#551) · C2 (#552-#553). **`api-types.ts` is now settled**, which is the precondition
>    phase 3 was waiting on: migrating a module against a contract about to change guarantees rework.
>    **Two of the four turned out not to be backend gaps at all** — `teamRanking` (#546) was a mapper discarding
>    data the API already sent, and BUG-311's export formats were a *second* description of a list the backend
>    already owned. Both were filed as "add the field"; both were fixed by deleting a duplicate description.
> 3. **FE per module** against the settled contract — B3/B4/B5 first slice, D1 finishes each. ← **NOW ACTIVE**
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
  **+ `offboarding/initiate/:employeeId`** (found by the B4 wiring audit, 2026-08-23) — reachable only by
  typing the URL; the natural entry point is the employee profile of a terminated employee.

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

- **2026-08-23 (C4)** — **C4 shipped (#561), diverging from its own prescription.** `Status == Active` would
  have blocked probation and suspension — a regression dressed as a fix. **That is now the fourth prescription
  in this queue that was wrong**, after A2 (two-thirds wrong), C2 (named a route that does not exist) and B4
  ("mirror the gate" would have re-created the defect). The pattern is no longer anecdotal: *a register entry's
  SYMPTOM is evidence; its PRESCRIPTION is a hypothesis someone wrote without re-reading the target.*
  Also: closing the class rather than the named instance found the same gap in `TrainingService`. Worth doing
  on every remaining item — the register lists instances, not classes.
- **2026-08-23 (later still)** — **C3 shipped (#559).** Decision taken and recorded; the register's site list was
  wrong in membership; and both audits found things that would have shipped. The one worth repeating: the
  change **could have been a no-op** and every test would have stayed green, because the arms asserted the row
  existed rather than that the reader could see it — a difference of exactly one predicate. *When a fix's thesis
  is "X is now visible to Y", the test has to go through Y.*
  The guard needed **four versions** before it could fail at all. Mutation-testing the GUARD, not just the code,
  is the only reason that was ever discovered.
- **2026-08-23 (later)** — **B5 shipped (#557).** Both halves were genuine, both were decided with the human
  toward the better option. Two findings worth carrying: the audit caught that I had **regenerated the contract
  before making the field nullable**, so the committed spec contradicted the very semantic the design rests on —
  regenerate LAST, after the final signature change, not mid-way. And mutation testing again found that the
  field a change exists for was the least guarded one, because every fixture used the passing value.
- **2026-08-23** — **B3 ticked without a PR; B4 shipped (#555).** B3 was **already done** — verified against the
  code before starting, and both its defects had been fixed by #524/#525/#545. Third stale entry caught by
  checking rather than trusting; building it would have been pure duplication.
  B4 was real, and its *prescription* was the wrong shape: "mirror the BE gate" would have re-created the
  two-descriptions-of-one-rule defect that caused it. The rule now lives once and is projected.
  **Two process notes worth keeping.** (1) Mutation testing found two gate clauses that no existing arm could
  kill — one masked by a service invariant, one by a global query filter — which is the second time in this
  programme that a surviving mutant marked a real gap rather than a pointless clause. (2) I contaminated my own
  first mutation run: the "sources restored" check grepped a string that also matched a second call site, so a
  stubbed projection went unnoticed and got baked into the backup, and every result after it was measured
  against a broken baseline. **Restore verification must be a checksum, not a grep.**
  Auditor findings folded in above; the envelope-coverage item needs a human call.
- **2026-08-22** — **Phase 2 (contract completion) closed.** Ticked **B6** (#550) and **C2** (#552/#553); the
  ISSUE-379 exposure adds (#546-#549) and `/leaves/reports/summary` (#551) landed alongside. `api-types.ts` is
  settled, so **phase 3 (FE per module) is unblocked** — B3 is the topmost open item.
  **Ticked in a separate commit, on purpose:** ticking inside each PR makes every concurrent PR conflict on this
  one file, which is the conflict the one-item rule exists to prevent.
  **Two corrections this phase forced into the register.** C2's prescription ("point them at the existing
  `/download` route") named a route that does not exist — the sixth prescription in this queue written without
  re-reading its target, and the third to be *inverted* by checking. And two of the four "backend field gaps"
  were duplicate descriptions on the FE side, not missing data. The pattern is now consistent enough to state
  plainly: **a finding's stated cause is unreliable in both directions; only its symptom is evidence.**
- **2026-08-17** — Queue created from the gap-analysis refresh §7. Iteration 0 probe done: **5 of 5 confirmed**, two worse than the static read (LOP has a second primary bug; self-assessment throws rather than empties). Bar set at all four legs; S-1 method set to generated types + explicit mappers. #509/#510/#511 merged, so the queue starts from a clean trunk.
