---
name: perf-volume-seed-conventions
description: How perf/seed/perf-volume-seed.sql is structured, its acme dependency, and the probation-cycle date trap in the Performance (WS-D) section
metadata:
  type: project
---

`perf/seed/perf-volume-seed.sql` layers per-module VOLUME onto the 5k `perf` tenant created by
`seed-perf-tenant.sql`. Sections: WS-A leave, WS-B attendance, WS-C recruitment+audit, **WS-D performance
(added 2026-09-07, ENH-013b)**. Conventions: one `\set ON_ERROR_STOP on` + single transaction, an idempotent
`DELETE ... WHERE tenant_id = :perf_tid` reset block at the top, `gen_random_uuid()` for bulk ids, fixed
literal uuids for rows that TCs/k6 must pin, and a closing count summary.

**Landmine — the file needs an `acme` tenant and does not say so.** WS-A copies `leave_types` from
`WHERE subdomain='acme'`; with no acme it copies zero rows and the run aborts at `leave_ledger`
(`null value in column "leave_type_id"`). `seed-perf-tenant.sql` was already hardened against exactly this
(DF-53, COALESCE across acme/e2e/any) but **`perf-volume-seed.sql` was not**. The local dev DB has
`techoneglobal`, not `acme`, so the whole file aborts there today.

**Trap in WS-D — do not re-date the probation cycle forward.** `ResolveCycleAsync` picks
`max(start_date)` ignoring status AND type, and `includeProbation` defaults to **false**. A probation cycle
dated after the current annual cycle becomes the default overview cycle, resolves to an EMPTY population, and
returns a fast, green, meaningless 200. `H2-2025 Probation` therefore starts 2025-07-01, behind FY2026.

**Volume shape rationale:** 4 non-probation cycles x reviews for all 5000, because `GetTrendAsync` re-runs
`LoadPopulationAsync` once PER CYCLE and trends ALL cycles when `cycleIds` is omitted — a single cycle would
leave that N+1 path measuring nothing. Observed counts: appraisal_cycle 5, cycle_participant 20,300,
self_assessment 20,300, manager_review 20,300, goal 60,900.

**How to apply:** verify seed changes on a throwaway Postgres built from `pg_dump --schema-only` of the dev
DB rather than the dev DB itself; snapshot row counts before/after if you must use the dev DB.
See [[perf-harness-rate-limit-ceiling]].
