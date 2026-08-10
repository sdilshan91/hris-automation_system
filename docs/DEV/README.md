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

## The FE↔BE contract pipeline (GAP-S1) — read this before changing a DTO

The Angular models are **generated from the API's own OpenAPI document**, not hand-written. This exists
because the contract drifted in **9 of 13 modules** when both sides were maintained by hand: the frontend
was coded against shapes the API cannot emit, and the Karma specs mocked the invented shape, so the suite
stayed green over dead features.

**Two commands, two committed artifacts:**

```bash
scripts/gen-openapi.sh                     # C# assembly  → contracts/openapi/hrm-v1.json
(cd src/frontend && npm run api:types)     # that document → src/app/core/api/generated/api-types.ts
```

`gen-openapi.sh` reads the **built assembly**, not a running server — no Kestrel, no port, no database — so
it works anywhere `dotnet build` does. Both artifacts are committed, and **both are gated in CI**
(`ci-gate.yml`): the backend job runs `gen-openapi.sh --check`, the frontend job runs `npm run api:types:check`.

**If you change a C# DTO or route, run both commands and commit both files** — otherwise CI fails with
`GAP-S1 CONTRACT GATE`. That failure is the feature: it lands at the point of change instead of as a runtime
400 in a module nobody is looking at.

Consuming the types (see [`core/api/index.ts`](../../src/frontend/src/app/core/api/index.ts) for the full guide):

```ts
import { Schema } from '@core/api';
type Employee = Schema<'EmployeesEmployeeDto'>;
```

Never hand-edit `generated/api-types.ts`. Two caveats worth knowing: schema ids carry Swashbuckle's
feature-namespace prefix (`EmployeesEmployeeDto`, not `EmployeeDto`), and **every property is optional**
because Swashbuckle emits no `required` for non-nullable C# reference types — so the types catch wrong and
misspelled fields, but do not yet prove a field is always present.

## Dashboards
[STATUS](STATUS.md) · [PLANS](PLANS.md) · [BLOCKERS](BLOCKERS.md) · [DECISIONS](DECISIONS.md) · [INSTRUCTIONS](INSTRUCTIONS.md)
