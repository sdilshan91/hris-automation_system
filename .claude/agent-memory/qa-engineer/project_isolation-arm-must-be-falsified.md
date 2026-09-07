---
name: isolation-arm-must-be-falsified
description: How to write a tenant-isolation test in this repo that is not theater — the two-layer split (EF filter vs real RLS), the mandatory vacuity guards, and the falsification step
metadata:
  type: project
---

An isolation test that has never been observed to fail is the same defect as the one it replaces.
ISSUE-303 was exactly this: `pg_policies` row existence asserted instead of behaviour.

**Why:** "a `tenant_isolation` policy row exists" and "tenant B cannot read tenant A's rows" are
different claims. A policy with a broken `USING` clause, or `USING (true)`, produces an identical
catalog row. Equally, "B sees 0 rows of A" is satisfied by a blanket-deny policy and by a seed that
silently inserted nothing — so the naive arm is green for three wrong reasons.

**How to apply** — every new module-level isolation arm needs all four:

1. **Two layers, two classes.** The EF global query filter is the control LIVE on a default deploy
   (RLS ships DORMANT — migrations `CREATE POLICY` with no `ENABLE ROW LEVEL SECURITY`; only
   `DbInitializer.ReconcileRowLevelSecurityAsync`, gated on `Rls:Enabled`, arms it). So: the EF-filter
   arm goes in the module's plain Postgres class; the RLS arm needs its OWN class + container because
   it hand-runs role DDL + `ENABLE`/`FORCE` across the whole DB. Harness to copy:
   `NotificationRlsPostgresTests` / `RlsIsolationPostgresTests` (create `hrm_app` NOBYPASSRLS +
   `hrm_owner` BYPASSRLS, migrate as superuser, GRANT, `ENABLE`+`FORCE` loop over `tenant_id` tables
   excl. users/tenants, then `set_config('app.current_tenant', @t, true)` per tx).
2. **Never read as the container superuser** — it always bypasses RLS, so the arm would stay green
   with every policy dropped. Read as `hrm_app`.
3. **Vacuity guards are load-bearing, not decoration.** (a) assert `relrowsecurity AND
   relforcerowsecurity` from `pg_class`, else you are testing a dormant table; (b) BYPASSRLS owner sees
   BOTH tenants' rows on disk, so "0 for B" cannot be a failed seed; (c) the SAME connection under
   tenant A's GUC DOES see A's rows — this is what separates "isolates" from "denies everything";
   (d) NO GUC at all reads zero (fail-closed).
4. **Falsify it before reporting.** Mutate isolation away (EF arm: add `IgnoreQueryFilters()` to the
   B-side read; RLS arm: swap the read connection to `_ownerConnString`), watch ONLY the isolation
   assertion go red while the controls stay green, then revert and verify with `sha256sum -c`.
   See [[verify-the-verifier]].

**Gotchas.** In the plain-EF arm prove separation by SWITCHING TENANT CONTEXT, never by
`IgnoreQueryFilters()` — bypassing the filter under test tests nothing; use it ONCE, only for guard
(b). `SharedPostgresFixtureIsolationTests` (HRM.ArchitectureTests) fails the build if a class using
`IClassFixture<PostgresContainerFixture>` invokes `IgnoreQueryFilters()` — classes owning their own
`IAsyncLifetime` container are exempt. FluentAssertions `Equal(params T[])` has **no** `because`
overload: `.Should().Equal(id, "reason")` fails to compile with "cannot convert string to Guid" —
use `BeEquivalentTo(new[] { id }, "reason")`.

Anchors 2026-09-07 (ISSUE-303): `FinalSettlementRlsPostgresTests` (RLS arm, one ordered `[Fact]` so the
gate pays for one container) + `FinalSettlementPostgresTests.Settlements_AndTheirLines_AreTenantIsolated_AcrossContexts_OnPostgres`
(EF arm). Both bound to `TC-PAY-013-07`.
