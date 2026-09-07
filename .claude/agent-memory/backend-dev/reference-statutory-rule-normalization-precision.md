---
name: statutory-rule-normalization-precision
description: Statutory-rule country_code normalization asymmetry (write/index/precheck/resolver disagree) + the numeric(5,2) rate-scale contract, and which harness proves each
metadata:
  type: reference
---

# Statutory rules: two recurring defect classes (ISSUE-169, ISSUE-299)

## 1. The country_code normalization asymmetry

Four places touch `statutory_rule.country_code` and they did NOT agree:

| Site | Normalizes? |
|---|---|
| `StatutoryRuleService` write paths (create/update/clone) | YES — `Trim().ToUpperInvariant()` |
| Unique index `(tenant, type, country_code, fiscal_year, effective_from)` | **NO — raw column** |
| Create duplicate pre-check / clone target-FY guard | was raw; **now normalized (ISSUE-299)** |
| `StatutoryDeductionResolver` + cumulative pre-scan + payroll reports | YES — `upper(btrim(...))` |

A row written outside the service (seed/import/psql) stores `'lk '`. Because the pre-check AND the index
were both raw, a second `'LK'` row was accepted; because the resolver is normalized, **both** then matched
and `SelectEffectiveByType` picked an arbitrary winner on equal `EffectiveFrom` → wrong tax rate on payroll.

**How to write the compare:** `r.CountryCode.Trim().ToUpper()` — `ToUpper()` translates to SQL,
`ToUpperInvariant()` does NOT (use the Invariant form only on already-materialized in-memory lists).
Mirror the resolver *exactly* (btrim AND upper); case-only `upper()` leaves the whitespace gap open.

**Residual after ISSUE-299:** the unique index is still on the raw column, so the DB is not a backstop —
two concurrent creates of `LK` vs `lk ` still slip past both layers. Closing that needs a migration to a
functional index on `upper(btrim(country_code))`.

## 2. numeric(5,2) rate scale

Tax-slab `rate_percentage` and social-security `employee_rate`/`employer_rate` are all `numeric(5,2)`.
`InclusiveBetween(0,100)` alone lets `12.345` through; **Postgres silently ROUNDS to 12.35 and the API
returns 201 with the rounded number** (the response is DB-fresh via `BuildDtoResultAsync`, so it is a
missing-validation defect, never a stale-echo one). Fix is `PrecisionScale(5, 2, ignoreTrailingZeros: true)`
— same idiom as ISSUE-152 on `AnnualCtc` (`numeric(18,2)`).

The two halves shipped in **separate** PRs — slab `RatePercentage` (ISSUE-169, PR #652) and
social-security `EmployeeRate`/`EmployerRate` (ISSUE-501) — and #652 was still absent from
`test/local-subdomains` on 2026-09-07. **Grep `PrecisionScale` in `CreateStatutoryRuleValidator.cs`
before assuming either half is present**; error code is `invalid_rate_scale`, arms live in
`HRM.Tests/Unit/StatutoryRuleAmountBoundsTests.cs`.

Related: `StatutoryLimits.MaxMonetary` is the **numeric(18,2)** ceiling, but exemption `Value`/`MaxAmount`
are `numeric(18,4)` — the shared constant is ~100x too permissive for those two columns.

## Which harness proves what

- `HRM.Tests/Integration/StatutoryRuleMultiCountryPostgresTests.cs` — real Postgres via Testcontainers,
  seeds dirty rows straight through the DbContext (`RawIncomeTax` helper bypasses the service). **Use this
  for anything asserting a normalized compare**: InMemory client-evaluates `Trim()/ToUpper()` and would green
  a fix that fails to translate. Container is per-class (`IAsyncLifetime`); the class runs ~3-5 min.
- `HRM.Tests/Integration/StatutoryRuleIntegrationTests.cs` — InMemory, hand-built `ServiceCollection`.
  **Gotcha: it registers MediatR but NOT `AddValidatorsFromAssembly` and NOT `ValidationBehavior`**, so
  FluentValidation does not run in that "pipeline" at all. A validation arm written there is a no-op that
  passes for the wrong reason. Validate the command object directly instead
  (`new CreateStatutoryRuleValidator().Validate(cmd)`), which also proves the nested
  `RuleForEach(...).SetValidator(new TaxSlabInputValidator())` wiring on both create and update.

See [[reference-payroll-audit-bug080]] for the audit-emitter side of this service.
