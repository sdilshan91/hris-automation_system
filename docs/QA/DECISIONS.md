# QA — DECISIONS

QA process/policy decisions + product decisions awaiting sign-off.

- **Testing loop is REPORT-ONLY** — `@test-runner` writes only to `docs/QA/` ledgers; never edits `src/`, never weakens a test, never opens a PR.
- **Never weaken/skip/delete a test to go green** — enforced by the `test-integrity-guard` hook.
- **Product decisions needed** → [`PRODUCT-DECISIONS-NEEDED-2026-07-05.md`](reports-archive/PRODUCT-DECISIONS-NEEDED-2026-07-05.md) · [`MEDLOW-TRIAGE-2026-07-05.md`](reports-archive/MEDLOW-TRIAGE-2026-07-05.md).
- **Architecture/domain decisions** affecting test design → indexed in [`../vault/decisions/`](../vault/decisions/).
