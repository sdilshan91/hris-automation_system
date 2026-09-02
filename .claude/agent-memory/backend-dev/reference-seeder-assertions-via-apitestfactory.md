---
name: reference-seeder-assertions-via-apitestfactory
description: ApiTestFactory really runs DbInitializer.RunAsync in Development — so assert on seeded ROWS, not on a re-declared copy of the seed constant
metadata:
  type: reference
---

`ApiTestFactory` (`src/backend/HRM.Tests/Integration/Http/ApiTestFactory.cs`) boots the genuine
`Program` host with `UseEnvironment(Development)` against a throwaway Postgres Testcontainer.
That means `DbInitializer.RunAsync` **really migrates and really seeds** — the `platform` tenant
(`SeedAsync`) *and* the DEV-only `e2e` tenant (`SeedE2EDevTenantAsync`) — before any test in the
shared `[Collection("HttpApi")]` runs.

**How to use it:** to guard anything the seeder WRITES, take a scope
(`_factory.Services.CreateScope()` → `AppDbContext`) and read the row back with
`IgnoreQueryFilters().AsNoTracking()`. No new container, no new fixture: joining the HttpApi
collection costs ~0 (the whole collection runs in ~2m20s). `PlanModulesSeedDriftApiTests` is the
worked example.

**Why this matters more than it looks.** A "seed guard" written as a unit test can only compare a
constant to itself — that is how
`PlanModulesEntitlementTests.SeedVocabulary_IsExactlyTheCanonicalModuleSet_ADM012` shipped as a
tautology (`var seeded = PlanModules.All;` … `.BeEquivalentTo(PlanModules.All)`) while its own
comment claimed it caught the ISSUE-335 regression. Mutation-checked 2026-09-02: repointing only
the seed **call sites** (leaving the shared member intact) is invisible to every unit arm and red
in all four DB-reading arms. If the assertion is about what the seeder *did*, it has to read the
database.

See also [[feedback-integration-tests-inmemory]] for the InMemory-vs-Testcontainers split.
