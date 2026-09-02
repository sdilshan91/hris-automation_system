# test-runner memory index

## Stack / environment (read before planning any run)
- [Local stack fixture constraints](local-stack-fixture-constraints.md) — no `acme` tenant, 3 users, DB creds, in-container Serilog path, why new personas can't be minted.

## Execution runs (per module/story)
- [US-PRF-005 360-release exec 2026-09-02](us-prf-005-360-release-exec-2026-09-02.md) — 0P/0F/3B; BUG-431 + ISSUE-432.
- [Performance FE pass 2026-06-30](performance-fe-pass-2026-06-30.md) — 2 blocked→pass; ISSUE-210 module unreachable from sidebar; in-page Router soft-nav trick.
- [Draft tenant-ISO sweep 2026-07-03](draft-iso-sweep-2026-07-03.md) — 22P/0F/3B; the cross-tenant probe pattern + how to tell guard-403 from authz-403.
- [Admin Console FE pass 2026-06-30](admin-console-fe-pass-2026-06-30.md) — 2 TC flips, 4 findings.
- [Admin ISO fixture exec 2026-06-30](admin-iso-fixture-exec-2026-06-30.md) — 7P/7F on a throwaway iso fixture.
- [US-ADM-002 monitoring run](us-adm-002-monitoring-run.md) — real routes vs blocked-by-design; audit verification recipe.
- [Auth regression 2026-06-27](auth-regression-2026-06-27.md) — all prior findings still present, zero new.
- [Payroll regression 2026-06-27](payroll-regression-2026-06-27.md) — no regressions; isolation header-override still live.
- [SSO epic exec 2026-06-27](sso-epic-test-exec-2026-06-27.md) — API arms blocked (BE down); happy path needs interactive Entra login.
- [US-ATT-001 clock-in](us-att-001-clockin-findings.md) · [US-ATT-003 regularization](us-att-003-regularization-findings.md) · [US-ATT-005 shifts](us-att-005-shift-findings.md) — attendance routes, personas, settings recipes.
- [US-REC-006 scorecard](us-rec-006-scorecard-findings.md) · [US-REC-010 convert](us-rec-010-convert-findings.md) — recruitment; convert broken on Postgres (BUG-068 CRIT).
