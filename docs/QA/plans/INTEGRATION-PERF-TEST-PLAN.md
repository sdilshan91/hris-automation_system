# Integration (Docker) & Performance (k6) Test Plan

> Phased plan to unblock two blocker categories from the QA backlog:
> **Track A — Docker-dependent integration tests** (Testcontainers.PostgreSql) and
> **Track B — performance / k6 / scale**. Companion to [QA-COVERAGE-PLAN.md](QA-COVERAGE-PLAN.md).
> **Report-only** for execution (log findings, never weaken a test). Update the **Status Tracker**
> and **Progress Log** as each phase starts/finishes.

## Status legend (maintain these as you go)
`[ ]` not-started · `[~]` in-progress · `[x]` done-clean · `[!]` done-with-findings · `[b]` blocked

## Prerequisites (verify before Phase 1 of either track)
- **Docker Desktop** — installed; currently **DOWN** (must be started for Track A).
- **k6** — installed ✅ (`k6.exe v2.0.0`).
- **PostgreSQL 18** running (`hris_dev_db`), **backend** running on `:5000`, personas seeded (`Admin@123!`).
- Decide the **target stack URL** for k6: `http://localhost:5000` (plain) or `https://acme.myhrm.org` (TLS rig).
- Artifacts land under a new **`perf/`** dir (k6 scripts + seed) — committed; results/CSV gitignored.

---

## TRACK A — Docker integration tests (Testcontainers)
**What/why:** `src/backend/HRM.Tests/Integration/*` (~15+ suites) use `Testcontainers.PostgreSql` to run
against a **real throwaway Postgres** — they were blocked only because Docker was down. These catch
Postgres-specific defects that the InMemory unit tests mask (the **BUG-068 class**: manual-tx vs
`EnableRetryOnFailure`, RLS, snake_case, real constraints).

| Phase | Goal | Tasks | Success criteria |
|---|---|---|---|
| **A0 — Docker up** | Daemon ready | Start Docker Desktop; wait for `docker ps` to succeed; pull `postgres` image if needed | `docker ps` returns 0 |
| **A1 — Run suite** | Execute integration tests | `dotnet test src/backend/HRM.sln --filter "FullyQualifiedName~Integration"` (no debugger) | Suite runs to completion (containers spin up) |
| **A2 — Triage** | Classify results | For each FAIL: real defect vs test-env issue; correlate via Serilog/exception. Log genuine defects to [TEST-FINDINGS.md](../TEST-FINDINGS.md) (next free IDs) | Every failure has a verdict + (if real) a finding ID |
| **A3 — Reconcile ledger** | Update blocked TCs | Map results to the markdown TCs that were `[b]` "needs Docker/Testcontainers"; flip to `[x]`/`[!]`; note in TEST-STATUS.md | Docker-blocked TCs re-classified |

> **Note:** This validates the **integration layer**. RLS-specific TCs (US-PLT-002) need the RLS
> feature *implemented* (Phase 4), not just Docker — those stay blocked here and are tracked separately.

---

## TRACK B — Performance / k6 / scale harness
**What/why:** the perf/scale arms (`-11` load, `-12`/scale across modules; ~82 "perf" / 34 "k6" TC
references) were blocked on **no k6 harness + no large seed**. k6 is now installed; we build the seed
+ scripts and run.

| Phase | Goal | Tasks | Success criteria |
|---|---|---|---|
| **B0 — Pre-flight** | Confirm tooling + target | k6 version OK; backend reachable; pick a **dedicated `perf` tenant** (NOT acme — avoid polluting real data) | Smoke `k6 run` of a 1-VU script returns 200s |
| **B1 — Bulk seed** | Realistic data volume | Seed the `perf` tenant via **direct SQL** with explicit `employee_no` (bypasses the broken generator, **BUG-093**): 1k then 5k employees + supporting rows (depts, attendance, leave). Idempotent + a teardown script | 5,000 employees in `perf` tenant; acme/techoneglobal untouched |
| **B2 — Author scripts** | Map TCs → k6 | One k6 script per scenario with `thresholds` from the TC SLAs: (1) hot reads (employee list, dashboard, tenant-context) @ 50 VU/5 min; (2) auth/login throughput; (3) scale reads/reports/exports at 5k; (4) bulk-import async boundary (≥1k rows → sync→async) | Scripts in `perf/`, parameterized by base URL + token |
| **B3 — Execute** | Run baseline → load → scale | Run each scenario; capture p95/throughput/error-rate (k6 summary + JSON out). Throttle CPU/network for slow-condition variants if a TC asks | Clean runs, metrics captured per scenario |
| **B4 — Analyze + findings** | Verdict vs SLA | Compare p95/error-rate to the TC thresholds. Log perf defects (slow endpoints, errors under load, async boundary not crossing) to TEST-FINDINGS.md. Flip the perf/scale TCs `[x]`/`[!]` | Each perf TC has a measured result + verdict |
| **B5 — Teardown** | Clean up | Run the seed teardown (delete `perf` tenant rows by exact tenant_id/PK). Verify zero residue in acme/techoneglobal | Perf seed removed; real tenants intact |

> **Safety:** seed/teardown touch ONLY the dedicated `perf` tenant, by its tenant_id/PK — never a
> blanket `tenant_id` delete on a shared tenant (per the 2026-06-27 policy).

---

## Recommended sequence
**A0 → A1 → A2 → A3** (Docker integration; ~quick), then **B0 → B1 → B2 → B3 → B4 → B5** (perf; longer).
Track A first (smaller, surfaces real Postgres bugs fast); Track B second (more setup).

---

## STATUS TRACKER (update as phases run)
| Track | Phase | Status | Started | Finished | Result / findings |
|---|---|---|---|---|---|
| A | A0 Docker up | `[x]` | 2026-06-30 | 2026-06-30 | Docker Desktop started; `docker ps` 0; `postgres:17-alpine` image present |
| A | A1 Run suite | `[x]` | 2026-06-30 | 2026-06-30 | **518/518 PASS, 0 fail, 0 skip** on real throwaway Postgres (had to stop the :5000 backend first — it locked the build DLLs) |
| A | A2 Triage | `[x]` | 2026-06-30 | 2026-06-30 | **No failures → no new findings.** Integration layer validated against real Postgres |
| A | A3 Reconcile ledger | `[!]` | 2026-06-30 | 2026-06-30 | Postgres-class findings re-checked: **BUG-068 (convert-to-employee) appears RESOLVED** (code refactored to single-SaveChanges + `BeginTransaction` guarded behind `IsRelational()`; `EnableRetryOnFailure(3)` set; `ApplicantConversionIntegrationTests` 11/11 green on real PG). Recommend confirming the original live API repro before formally closing. No TEST-STATUS TC was marked "needs Docker" — the blocker was the xUnit suite itself, now green |
| B | B0 Pre-flight | `[x]` | 2026-06-30 | 2026-06-30 | k6 v2.0.0 OK; backend :5000 reachable; **`perf` tenant chosen** (id `11111111-2222-3333-4444-555555555555`, none pre-existing); k6 smoke 5/5 checks PASS |
| B | B1 Bulk seed | `[x]` | 2026-06-30 | 2026-06-30 | **5,000 employees** seeded via direct SQL in 1.3s (10 depts, 8 titles, 8 roles copied from acme, `perfadmin@perf.test`). Login + read verified (headcount report = 5000). acme/techoneglobal untouched |
| B | B2 Author scripts | `[x]` | 2026-06-30 | 2026-06-30 | `perf/scripts/`: smoke, 01-hot-reads (50VU/5m), 02-auth-login (→20VU/2m), 03-scale-reads (30VU/3m @5k), 04-bulk-import-boundary (500/600). Thresholds from TC SLAs (list p95<400, reports<800, export<2000, err<1%) |
| B | B3 Execute | `[x]` | 2026-06-30 | 2026-06-30 | All 4 scenarios ran; metrics in `perf/results/`. hot-reads + scale-reads + import-boundary clean; login threshold crossed (captured) |
| B | B4 Analyze + findings | `[!]` | 2026-06-30 | 2026-06-30 | **2 new findings: BUG-095** (MED — report export 500 under concurrent same-second exports, file-name collision, `LocalReportExportStorage.cs:41`) + **ISSUE-203** (MED — login p95 3.86s @20VU vs 800ms SLA, BCrypt(12) CPU-bound). Reads meet SLA (list p95 145ms; scale list p95 141ms; reports ~75ms). Import boundary verified both sides. Aggregation exact (5000) |
| B | B5 Teardown | `[x]` | 2026-06-30 | 2026-06-30 | Teardown by exact tenant_id — **all perf residue = 0**, perf tenant removed. acme intact (34 employees), techoneglobal intact (1 — pre-existing orphan, not from this run) |

## PROGRESS LOG (append a row each session)
| Date | Phase(s) | What happened | Outcome |
|---|---|---|---|
| _2026-06-30_ | — | Plan created | Ready to start |
| _2026-06-30_ | A0–A3 | Started Docker; ran integration suite (had to stop :5000 backend first — DLL lock). **518/518 PASS** on real Postgres. No failures → no findings. Re-checked Postgres-class findings: BUG-068 appears resolved (single-SaveChanges refactor + IsRelational guard + EnableRetryOnFailure; 11/11 conversion ITs green). Restarted backend on :5000 (needed `ASPNETCORE_ENVIRONMENT=Development` so user-secrets load). | **Track A done** — A0/A1/A2 clean, A3 done-with-note |
| _2026-06-30_ | B0–B5 | Built the `perf/` k6 harness + direct-SQL seed; seeded a dedicated `perf` tenant with 5,000 employees; ran 4 scenarios; analyzed vs TC SLAs; tore down to zero residue. Reads meet SLA comfortably; **2 new findings: BUG-095** (export 500 under concurrent same-second exports — filename collision) + **ISSUE-203** (login p95 3.86s @20VU, BCrypt(12) CPU-bound). Import sync→async boundary verified both sides. | **Track B done** — all phases; B4 done-with-findings |

---

## ✅ Plan complete (2026-06-30)
**Track A:** 518/518 integration tests pass on real Postgres; BUG-068 appears resolved (verify the live repro before formally closing). **Track B:** perf harness built (`perf/`), 5k-employee `perf` tenant load-tested and torn down clean; **BUG-095** + **ISSUE-203** logged to [TEST-FINDINGS.md](../TEST-FINDINGS.md). Per report-only policy, nothing was fixed — these findings are input to a separate, human-decided fix cycle.

---

## Risks & notes
- **Docker startup** can take 60–90s; first container pull adds time. If Docker won't start, Track A is blocked (Track B is independent and can proceed).
- **BUG-093** breaks the employee-create generator — **seed via direct SQL with explicit `employee_no`**, not the API, or seeding itself fails.
- **Don't seed into acme/techoneglobal** — use a throwaway `perf` tenant so real test data + the 462-TC baseline stay clean.
- **k6 target:** prefer `http://localhost:5000` for raw API perf (removes nginx/TLS overhead from the measurement); use the TLS rig only if testing the full proxy path.
- **Out of scope here:** RLS Phase 4 (US-PLT-002) and interactive SSO — separate efforts (see QA-COVERAGE-PLAN / SSO docs).
