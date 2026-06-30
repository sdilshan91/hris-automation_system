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
| **A2 — Triage** | Classify results | For each FAIL: real defect vs test-env issue; correlate via Serilog/exception. Log genuine defects to [TEST-FINDINGS.md](TEST-FINDINGS.md) (next free IDs) | Every failure has a verdict + (if real) a finding ID |
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
| A | A0 Docker up | `[ ]` | | | |
| A | A1 Run suite | `[ ]` | | | |
| A | A2 Triage | `[ ]` | | | |
| A | A3 Reconcile ledger | `[ ]` | | | |
| B | B0 Pre-flight | `[ ]` | | | |
| B | B1 Bulk seed | `[ ]` | | | |
| B | B2 Author scripts | `[ ]` | | | |
| B | B3 Execute | `[ ]` | | | |
| B | B4 Analyze + findings | `[ ]` | | | |
| B | B5 Teardown | `[ ]` | | | |

## PROGRESS LOG (append a row each session)
| Date | Phase(s) | What happened | Outcome |
|---|---|---|---|
| _2026-06-30_ | — | Plan created | Ready to start |

---

## Risks & notes
- **Docker startup** can take 60–90s; first container pull adds time. If Docker won't start, Track A is blocked (Track B is independent and can proceed).
- **BUG-093** breaks the employee-create generator — **seed via direct SQL with explicit `employee_no`**, not the API, or seeding itself fails.
- **Don't seed into acme/techoneglobal** — use a throwaway `perf` tenant so real test data + the 462-TC baseline stay clean.
- **k6 target:** prefer `http://localhost:5000` for raw API perf (removes nginx/TLS overhead from the measurement); use the TLS rig only if testing the full proxy path.
- **Out of scope here:** RLS Phase 4 (US-PLT-002) and interactive SSO — separate efforts (see QA-COVERAGE-PLAN / SSO docs).
