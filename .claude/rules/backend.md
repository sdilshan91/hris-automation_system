---
paths:
  - "src/backend/**"
---

# Backend rules (ASP.NET Core 10 · `src/backend`)

## Commands
```bash
dotnet restore HRM.sln
dotnet build HRM.sln
dotnet run --project HRM.Api          # API + Swagger at /swagger, Hangfire dashboard at /hangfire (dev only)

# Tests — ALWAYS via the wrapper, never raw `dotnet test`
bash scripts/run-backend-tests.sh src/backend/HRM.sln
COVERAGE=1 bash scripts/run-backend-tests.sh src/backend/HRM.sln   # line coverage (slower; CI turns this on)

# EF Core migrations (run from src/backend; --startup-project supplies config/connection string)
dotnet ef migrations add <Name> --project HRM.Infrastructure --startup-project HRM.Api
dotnet ef database update --project HRM.Infrastructure --startup-project HRM.Api
```

Migrations are **applied automatically on startup** via `DbInitializer.RunAsync` (`Program.cs`), which
also seeds a default admin tenant, roles, and admin user. **Never hand-write a migration** — CLI only.

## Tests

`src/backend/HRM.Tests` (xUnit + FluentAssertions + NSubstitute) — 360 unit and 215 integration test
files. Integration tests run against a **real PostgreSQL via Testcontainers**;
`Microsoft.EntityFrameworkCore.InMemory` is also referenced but is a known root-cause class for false
greens (InMemory masks Postgres behaviour — see `/fault-diagnosis`), so prefer the Testcontainers path
for anything touching SQL, query filters, or migrations.

> **Never invoke `dotnet test` directly.** It can exit **0 even when the run ABORTS** (test-host crash,
> killed process, resource contention), so a partial run is indistinguishable from a green suite to CI,
> `/implement-all`, or an agent — this has already hidden a real regression (ISSUE-312). Always go
> through `scripts/run-backend-tests.sh`, which forces a non-zero exit on any VSTest abort marker.
> Coverage is **measure-only** (`COVERAGE=1`); no threshold is enforced yet, deliberately — setting a
> gate before anyone has seen the number is how the gate ends up lowered.

## Nullability

All 5 projects set `<Nullable>enable</Nullable>`, so the build really does emit
**CS8602 / CS8604 / CS8714** — they are live warnings in this codebase, not theory.
Write null-aware C#: guard or annotate rather than reaching for `!`. The
`csharp-nullable-reference-types` skill has the full attribute catalog
(`NotNullWhen`, `MemberNotNull`, `MaybeNull`, …) when you need it.

## Clean Architecture + CQRS

Four projects, dependencies point inward (`Api → Application → Domain`; `Infrastructure → Application`):

- **HRM.Domain** — entities, value objects (e.g. `Email`), repository interfaces. No framework deps.
- **HRM.Application** — CQRS handlers by feature (`Features/{Feature}/Commands|Queries|DTOs|Validators`),
  MediatR pipeline behaviors (`ValidationBehavior`, `LoggingBehavior`), and `Common/Interfaces`
  abstractions (`ITenantContext`, `ICurrentUser`, `IJwtService`, `IAuthService`).
- **HRM.Infrastructure** — EF Core `AppDbContext`, entity configurations, interceptors, implementations.
  Wired in `DependencyInjection.AddInfrastructure`.
- **HRM.Api** — thin controllers dispatching via MediatR, middleware, filters, Hangfire jobs.
  Composition root is `Program.cs`.

Request flow: validation runs via both the MVC `ValidationFilter` and the MediatR `ValidationBehavior`;
`ExceptionHandlingMiddleware` is the outermost layer and normalizes errors.

## Multi-tenancy — three coordinated layers

**Critical Rule #1 in CLAUDE.md says tenant isolation is non-negotiable. This is how it is enforced.**
When adding an entity or a query, all three layers matter:

1. **Resolution** (`TenantResolutionMiddleware`, before auth): extracts the tenant from the request
   **subdomain** (`acme.yourhrm.com` → `acme`; `admin.*` → system context; reserved subdomains skip
   resolution), looks it up, populates the scoped `ITenantContext`. Dev fallback: the SPA sends
   `X-Tenant-Subdomain` (set by the frontend `tenantInterceptor`) so `*.localhost` hosts entries aren't needed.
2. **Write isolation** (`TenantInterceptor`, a `SaveChanges` interceptor): auto-stamps `TenantId` on any
   new `BaseEntity` when a tenant is resolved.
3. **Read isolation** (global query filters in `AppDbContext.OnModelCreating`): every tenant-scoped
   entity is filtered by `TenantId == _tenantContext.TenantId`. Use `IgnoreQueryFilters()` only
   deliberately (e.g. the tenant lookup in the resolution middleware itself).

`AuditInterceptor` stamps audit fields the same way. EF uses PostgreSQL with **snake_case** naming
(`EFCore.NamingConventions`).

> **The EF required-navigation trap.** Never `Include` a REQUIRED navigation whose principal is
> query-filtered: EF emits an INNER JOIN, and a filtered-out principal makes the dependent row vanish
> too. Configure the navigation optional (`IsRequired(false)` → LEFT JOIN) or put a matching filter on
> both entities.

## Cross-cutting infrastructure

- **Auth**: JWT bearer; `JwtService` is a singleton that also supplies `TokenValidationParameters`.
  BCrypt for password hashing. Refresh tokens cleaned up daily by the `TokenCleanupJob` Hangfire job.
- **Background jobs**: Hangfire on PostgreSQL storage; dashboard at `/hangfire` (dev only).
- **Resilience**: a named `ResilientClient` HttpClient with Polly retry + circuit-breaker for outbound calls.
- **Logging**: Serilog; `TenantId`/`TenantSubdomain`/`RequestId` are pushed into the log context per
  request. Daily rolling structured file at `src/backend/HRM.Api/Logs/hrm-<YYYYMMDD>.log` (console +
  file, exception + stack included). In **Development** the level is raised for root-causing: `HRM.*` at
  **Debug**, EF Core SQL (`Microsoft.EntityFrameworkCore.Database.Command`) at Information — base
  `appsettings.json` stays Information-only for prod. **QA/debug practice:** `@test-runner` and
  `@browser-debugger` read this log (correlating by `RequestId`) to pull the real exception/stack/SQL
  behind a failing TC — never infer root cause from the HTTP body alone when a log line exists.
  Requires a backend restart after changing the logging config.
