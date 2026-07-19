# Auditor Findings Log

**Purpose.** A single, durable log of every finding raised by the read-only review agents —
`@integration-enforcer` (wiring `OUT-OF-LANE` / GAPS) and `@test-authenticator`
(`MISSING (should exist, don't)` coverage arms + weak/theatrical arms) — together with its
**resolution status**.

These findings are auto-healed piecemeal: **fixed inline** in the same PR before merge, or **filed as a
`DF-##` row** in [`DEFERRED-FOLLOWUPS.md`](DEFERRED-FOLLOWUPS.md). That works, but it scatters them, and a
LOW-severity item judged "acceptable for now" can silently fall through. This log makes the full set
visible in one place so **nothing is lost** — including the deliberately-accepted ones.

**Convention (how this stays useful).**
- Log a row for **every** `@integration-enforcer` OUT-OF-LANE/GAP and **every** `@test-authenticator`
  MISSING/weak-arm finding, **at the time it is raised** (during the gate for a cluster/PR).
- **Status vocabulary:**
  - `FIXED-INLINE (#PR)` — resolved in the same PR before merge (the orchestrator adds the material arm / the dev fixes the code).
  - `FILED (DF-##)` — deferred; tracked as a `DF-##` row in `DEFERRED-FOLLOWUPS.md` (one hop to full context).
  - `ACCEPTED` — judged not worth acting on, **with a reason** (this is the class that used to fall through).
  - `OPEN` — raised, not yet triaged/resolved.
  - `RESOLVED (#PR)` — a previously-`FILED`/`OPEN` item later fixed; update the row.
- When a `FILED` item's DF row is later picked up, flip both this row (`RESOLVED`) and the DF row.
- Skim this before trusting a "green" cluster or when planning a coverage/hardening batch.

Finding types: **OOL** = enforcer OUT-OF-LANE/GAP · **MISS** = authenticator "MISSING (should exist, don't)" · **WEAK** = authenticator weak/theatrical arm.

---

## Findings

| Date | PR / cluster | Auditor · type | Finding | Sev | Status |
|------|--------------|----------------|---------|-----|--------|
| 2026-07-19 | #384 · DF-41/42 | authenticator · MISS | No arm proves the offer magic-link renders on the primary PDF path (only the dispatcher-fallback payload was inspected). | MED | **FIXED-INLINE (#384)** — added a catalog presence arm asserting `offer_sent` renders `{{offer.portalUrl}}`. |
| 2026-07-19 | #385 · DF-33 | authenticator · WEAK | The `ChronicThreshold == 0` disable-guard arm seeded `prior:6` → the `==threshold+1` arithmetic masked the guard (a guard-removal mutant survived). | MED | **FIXED-INLINE (#385)** — reseeded to `threshold:0, prior:0` so only the `>0` guard suppresses the fire. |
| 2026-07-19 | #385 · DF-33 | backend/authenticator · OOL | `AttendanceService.ClockInAsync` stamps `DateTime.UtcNow` — no `TimeProvider` clock seam (test-determinism; ~1s/month month-end window). | LOW | **FILED (DF-43)** |
| 2026-07-19 | #385 · DF-33 | enforcer/authenticator · MISS | No real-Postgres arm for the crossing count/local-date guard + no composition-root assert that DI injects a non-null `ILateEarlyService`. | LOW | **FILED (DF-44)** |
| 2026-07-19 | #386 · DF-38/39 | enforcer · OOL | **GAP-A:** `getEmploymentTypes` returned `{id,name,displayName}` typed as `{value,label}` with no `map()` → the Employment Type dropdown was **blank/undefined in prod**, masked by a wrong-shape spec mock. | HIGH | **FIXED-INLINE (#386)** — added the `map()` + corrected the masking mock. |
| 2026-07-19 | #386 · DF-38/39 | enforcer/authenticator · OOL | **GAP-B:** FE sub-section send-mapping dropped the row `id` (churned PKs) + `fieldOfStudy`/`startYear`/`description` (data loss on edit). | MED | **FIXED-INLINE (#386)** |
| 2026-07-19 | #386 · DF-38/39 | authenticator · MISS | WorkHistory/Dependents full-replace arms didn't prove removal-of-omitted (+ vacuous `OnlyContain`); no cross-tenant isolation arm on the 3 new sub-tables. | MED | **FIXED-INLINE (#386)** — orchestrator added the removal + isolation arms. |
| 2026-07-19 | #386 · DF-38/39 | authenticator · MISS | Real-Postgres arm for the 3 new sub-tables (delete-then-reinsert-same-PK, `DateOnly`→`date`). | MED | **FILED (DF-45)** |
| 2026-07-19 | #387 · BUG-056 | enforcer · OOL | Finalize moves goals `Acknowledged→Finalized`, but 3 "active = Submitted‖Acknowledged" filters (`GoalProgressService` ×2, `StaleGoalNudgeService`) omitted Finalized → a finalized set **silently vanished from progress tracking + stale nudges**. | MED | **FIXED-INLINE (#387)** — added `Finalized` to the 3 filters + a regression arm. |
| 2026-07-19 | #387 · BUG-056 | backend · OOL | `AggregateStatus` mapped a finalized set to the `"Draft"` team-dashboard bucket. | LOW | **FIXED-INLINE (#387)** — added a `Finalized` branch + test. |
| 2026-07-19 | #387 · BUG-056 | authenticator · MISS | Empty-set→422 boundary; `Update`/`Delete` write-guard arms (only Save+Create were proven). | LOW | **FIXED-INLINE (#387)** — orchestrator added the arms. |
| 2026-07-19 | #387 · BUG-056 | enforcer/backend · OOL | A finalized goal set has **no re-open/unlock path** (a mistaken finalize is unrecoverable). | MED | **FILED (DF-46)** — ⚖ decision-gated. |
| 2026-07-19 | #387 · BUG-056 | authenticator/enforcer · MISS | Real-Postgres/HTTP finalize round-trip arm + `Goal.Finalized` audit-row assertion. | LOW | **FILED (DF-47)** |
| 2026-07-19 | #388 · DF-17 | authenticator · MISS | No explicit zero-HTTP arm on a delivered depth-2 **leaf**; no fallback arm for a truncated node **inside** the delivered depth. | LOW | **ACCEPTED** — the core "zero-HTTP on in-depth expand" crux is mutation-proof; the fallback is proven at the depth-1 boundary. Noted in the #388 PR. |
| 2026-07-19 | #389 · ISSUE-021 | enforcer · OOL | `JobTitleDto` had no `GradeName` → the job-title list rendered "—" for every grade. | MED | **FIXED-INLINE (#389)** — added `GradeName` + a batched (no-N+1) `SalaryGrade` join in the read paths. |
| 2026-07-19 | #389 · ISSUE-021 | enforcer · OOL | The `salary-grades` route guard omitted `'Tenant Owner'` (present on sibling admin routes). | LOW | **FIXED-INLINE (#389)** — added `'Tenant Owner'` + a nav item. |
| 2026-07-19 | #389 · ISSUE-021 | authenticator · MISS | No cross-tenant grade-link rejection arm (a tenant-B grade must be rejected for a tenant-A JobTitle — BUG-003 class). | MED | **FIXED-INLINE (#389)** — orchestrator added the arm. |
| 2026-07-19 | #389 · ISSUE-021 | authenticator · MISS | Real-Postgres arm for `salary_grades` `decimal(18,2)` precision + `Code` uniqueness collation. | MED | **FILED (DF-48)** |
| 2026-07-19 | #390 · ISSUE-285a | enforcer/authenticator · MISS | The index-backed SQL, the migration **backfill SQL**, and the anniversary `.Month`/`.Day` translation are InMemory-only; a post-query re-filter means deleting the SQL `Where` keeps InMemory tests green (the optimization isn't pinned). | MED | **FILED (DF-49)** |
| 2026-07-19 | #390 · ISSUE-285 | (deferred by design) · — | (b) parallelize the ~8 sequential dashboard widgets via `IDbContextFactory` (concurrency + cross-tenant-in-factory risk). | MED | **FILED (DF-50)** |
| 2026-07-19 | #390 · ISSUE-285 | (deferred by design) · — | (c) k6 dashboard-at-scale SLA scenario (env-gated: 50k seed + running stack). | LOW | **FILED (DF-51)** |
| 2026-07-19 | #390 · ISSUE-285a | enforcer · OOL | Anniversary arm is a seq-scan (no denormalized `JoinMonthDay` column/index) — functional but unindexed. | LOW | **ACCEPTED** — birthday index (the ISSUE-285a target) is done; anniversary index folded into DF-49 as an optional follow-up. |
| 2026-07-20 | DF-46 · goal-reopen | authenticator · MISS | No positive arm for the BR-4 **direct-manager** (`SetGoal.Team`) re-open allow-path — only HR-happy + non-manager-403 existed; a mutant requiring `SetGoal.All` (dropping the manager branch) would survive. | HIGH | **FIXED-INLINE (#PR-pending)** — orchestrator added `Reopen_ByDirectManager_WithSetGoalTeam_Succeeds`. |
| 2026-07-20 | DF-46 · goal-reopen | authenticator · MISS | No **cross-tenant isolation** arm (BUG-003 class) — a tenant-B caller must not re-open tenant-A's finalized set; relies solely on the global query filter, untested. | HIGH | **FIXED-INLINE (#PR-pending)** — orchestrator added `Reopen_CrossTenant_CannotUnlockAnotherTenantsSet`. |
| 2026-07-20 | DF-46 · goal-reopen | authenticator · WEAK | The writability-restore arm asserted post-reopen SaveGoals succeeds but never showed the pre-reopen 409, so "restores writability" leaned on an untested guard; `OnlyContain` could pass vacuously. | MED | **FIXED-INLINE (#PR-pending)** — added the pre-reopen 409 contrast + `HaveCount(2)`. |
| 2026-07-20 | DF-46 · goal-reopen | authenticator · MISS | Audit arm proved `Detail.Contains(reason)` but not the BE-side `.Trim()` — a drop-`.Trim()` mutant survived (only the FE trim was proven). | LOW | **FIXED-INLINE (#PR-pending)** — reason seeded with surrounding spaces; asserts `"Reason: spaced reason"` (trimmed). |
| 2026-07-20 | DF-46 · goal-reopen | enforcer · OOL | FE `reopenGoals` typed `Observable<void>` / doc "204/void", but BE returns 200 `ApiResponse<EmployeeGoalsDto>`; FE ignores the body + reloads (harmless). Same latent drift as the `finalizeGoals` sibling. | LOW | **ACCEPTED** — cosmetic; consistent with the existing finalize sibling, feature works. |
| 2026-07-20 | DF-46 · goal-reopen | enforcer · ENH | The "Re-open" button renders for any viewer of a locked set (no FE permission gate); server 403s an unauthorized caller. Deliberate — the whole performance feature has no FE permission signal, and Finalize is likewise not FE-gated. | LOW | **ACCEPTED** — secure (server-enforced); matches the existing finalize pattern. Candidate for a future FE-permission-plumbing pass across performance actions. |
| 2026-07-20 | DF-46 · goal-reopen | enforcer · TEST-HEALTH | No full-chain controller→MediatR→ValidationBehavior→GoalService HTTP arm (validator-rejects-empty-reason proven by service+validator in isolation, not end-to-end). Repo standard (Testcontainers HTTP not sandbox-runnable). | LOW | **FILED (DF-47)** — folded the reopen HTTP round-trip into the existing goal-finalize Postgres/HTTP DF-47. |
| 2026-07-20 | DF-46 · goal-reopen | (decision, now built) · — | The finalize/lock had no undo (originally filed DF-46). | MED | **RESOLVED (#393)** — shipped `ReopenGoalsAsync` + endpoint + FE + 9 xUnit / 7 Karma arms; decision locked (HR or finalizing manager, mandatory reason, →Acknowledged). |
| 2026-07-20 | DF-14 · pending-approvals | authenticator · MISS | Newest-first ordering (`OrderByDescending(PayYear).ThenBy…(PayMonth)`) untested — every seeded run used the same period, so a drop-ordering mutant survived. | MED | **FIXED-INLINE (#PR-pending)** — added `GetPending_OrdersNewestPeriodFirst` (3 runs across 2 years/2 months). |
| 2026-07-20 | DF-14 · pending-approvals | authenticator · MISS | `ResolveSubmitterNamesAsync` User/email fallback branch + Employee-over-User precedence untested (only the Employee path was proven). | MED | **FIXED-INLINE (#PR-pending)** — added `…FallsBackToUserDisplayName` + `…PrefersEmployeeOverUser`. |
| 2026-07-20 | DF-14 · pending-approvals | authenticator · MISS | Distinct-approver had only an exclude arm; no positive control that a multi-step run the caller has NOT approved IS listed. | LOW | **FIXED-INLINE (#PR-pending)** — added `GetPending_IncludesMultiStepRun_CallerHasNotYetApproved`. |
| 2026-07-20 | DF-14 · pending-approvals | authenticator/enforcer · GAP | No real-Postgres/Testcontainers + HTTP-route arm for `GET /payroll/approval/pending` (isolation proven on the EF filter, not RLS; the route + `Payroll.Approve` gate only FE-mocked). | MED | **FILED (DF-52)** |
| 2026-07-20 | DF-14 · pending-approvals | enforcer · ISSUE | Distinct-approver check fires one `PayrollApprovalHistories` query per included multi-step run (bounded N+1); name + role lookups are batched. | LOW | **ACCEPTED** — bounded; AwaitingApproval multi-step runs are typically few. Optional pre-load noted in DF-52. |
| 2026-07-20 | DF-14 · pending-approvals | enforcer · ISSUE | Queue builds the step→role map with `ToDictionaryAsync` (throws on duplicate `StepNumber`) vs ApproveAsync's `FirstOrDefault`. | LOW | **ACCEPTED — moot** — verified a unique index on `(TenantId, StepNumber)` (`PayrollApprovalStepConfigConfiguration.cs`) makes a duplicate impossible. |
| 2026-07-20 | DF-14 · pending-approvals | (feature, now built) · — | FE Pending-Approvals queue called `GET /payroll/runs?status=…` which the BE ignored → every approver saw all runs (originally filed DF-14). | MED | **RESOLVED (#PR-pending)** — approver-scoped `GET /payroll/approval/pending` mirroring the ApproveAsync gates via a shared predicate; 12 xUnit / 3 FE arms; enforcer WIRED, authenticator 100%. |
