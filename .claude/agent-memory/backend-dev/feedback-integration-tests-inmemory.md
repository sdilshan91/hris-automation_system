---
name: feedback-integration-tests-inmemory
description: HRM.Tests has TWO integration conventions — InMemory (*IntegrationTests) and real-Postgres Testcontainers (*PostgresTests); Docker IS available locally now
metadata:
  type: feedback
---

`HRM.Tests/Integration/` has **two** coexisting conventions — pick by what you're proving:

1. **`*PostgresTests.cs` — real Postgres via Testcontainers** (`new PostgreSqlBuilder("postgres:17-alpine")`).
   As of 2026-07-24 **Docker IS available in this environment** and these tests **pass** (e.g.
   `TenantSsoSettingsPostgresTests`, `BenefitPlanPostgresTests`, `AuthConcurrentLockoutPostgresTests` —
   dozens of them). Use this whenever the thing under test is **provider-specific**: jsonb columns, `text[]`,
   partial unique indexes, RLS, concurrency/lost-update races, or EF global-query-filter tenant isolation you
   want proven against the real engine. Schema via `MigrateAsync()` (applies your generated migration — the
   faithful path) or `EnsureCreatedAsync()` (avoids `PendingModelChangesWarning` if an unrelated model change
   is uncommitted). Construct the service directly with an Npgsql `AppDbContext` + `TenantInterceptor` +
   `AuditInterceptor`; a `MutableTenantContext` flips the acting tenant to prove isolation.

2. **`*IntegrationTests.cs` — EF InMemory through the real DI/MediatR pipeline.** Fine for
   pipeline/handler/validation wiring that is NOT provider-specific. Faster, no Docker needed.

**Correction to the older note:** the blanket "Do NOT use Testcontainers, even when asked" was
environment-stale — it assumed no Docker/PG. That is no longer true here. When a story explicitly asks for
Testcontainers + real Postgres (and the behavior is provider-specific), **use it** and mirror an existing
`*PostgresTests.cs`. Tag with `[Trait("TC","TC-XXX")]`. See [[reference-benefits-module]].
