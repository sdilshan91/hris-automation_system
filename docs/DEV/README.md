# DEV — Engineering / Build & Run

Build/run/CI conventions and tooling. The operational config it points to (`local-dev/`,
`ops/`, `perf/`) stays at the repo root (wired into nginx/Docker/CI).

## What's here
- **[`PRODUCTION-CHECKLIST.md`](PRODUCTION-CHECKLIST.md)** — go-live gate. The RLS flip (`roles.sql`,
  ownership, grants, two-step deploy, rollback), required secrets + the Data-Protection ring backup rule,
  ClamAV fail-closed, GlitchTip hardening, SSO/capacity checks, and the one open business decision (BUG-291).
  Everything here needs credentials or a maintenance window — none of it can be done from the repo.
- **[`TOOLING-ADOPTION-PLAN.md`](TOOLING-ADOPTION-PLAN.md)** — tooling roadmap.
- Links to operational config: [`../../local-dev/`](../../local-dev/) (nginx/certs), [`../../ops/`](../../ops/), [`../../perf/`](../../perf/) (k6).

## Dashboards
[STATUS](STATUS.md) · [PLANS](PLANS.md) · [BLOCKERS](BLOCKERS.md) · [DECISIONS](DECISIONS.md) · [INSTRUCTIONS](INSTRUCTIONS.md)
