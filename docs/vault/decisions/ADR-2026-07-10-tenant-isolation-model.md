---
title: ADR — Tenant Isolation Model (RLS vs Database-per-Tenant)
status: Proposed
date: 2026-07-10
deciders: Product Owner, Platform Engineering
tags: [multi-tenancy, architecture, postgres, ef-core, security, rls]
supersedes: none
related:
  - "[[ADR-2026-07-08-saas-data-governance-posture]]"
  - "user-stories/platform/US-PLT-002.md"
  - "src/backend/HRM.Infrastructure/Persistence/Rls/README.md"
---

# ADR: Tenant Isolation Model — Postgres RLS vs Database-per-Tenant

> Status: **Proposed** (advisory / decision-support). A human accepts this ADR.
> This is a research advisory produced by `/advisor`; it edits no `src/`.

## Context

The platform is a multi-tenant HRM SaaS (Angular 20 + ASP.NET Core 10 + EF Core 10 +
PostgreSQL). Today tenant isolation is **shared-database, shared-schema, row-discriminator
(`TenantId`)** enforced in three layers:

- **Read isolation** — 109 EF Core global query filters
  (`grep HasQueryFilter src/backend` → 109 in `AppDbContext.cs`), one per tenant-scoped entity.
- **Write isolation** — `TenantInterceptor` stamps `TenantId` on new `BaseEntity` rows.
- **Planned backstop** — PostgreSQL Row-Level Security (RLS), scaffolded but **inert**
  (`Rls:Enabled=false`, `appsettings.json:7-9`).

The Product Owner asks whether, **instead of RLS**, we should move to
**database-per-tenant** (a separate database — or schema — per tenant) with **automated DB
provisioning on tenant onboarding** (create DB → run migrations → seed), and whether our
current infrastructure supports that.

### What the current code assumes a single database (evidence)

| Assumption | Evidence | Why it blocks DB-per-tenant |
|---|---|---|
| One DbContext → one connection string | `DependencyInjection.cs:43-45` — `AddDbContext<AppDbContext>` bound to `GetConnectionString("DefaultConnection")`; **Scoped**, not pooled, not a factory | Per-tenant DBs need the connection string resolved **per request** at DbContext creation |
| Isolation is query-filter based | 109 `HasQueryFilter` in `AppDbContext.OnModelCreating` | Redundant once each DB holds one tenant (kept as defense-in-depth, but no longer the mechanism) |
| One database is migrated + seeded on startup | `DbInitializer.RunAsync` → `MigrateAsync` + `SeedAsync` on the single resolved `AppDbContext` (`Program.cs:544`) | Must become **migrate + seed across N databases** |
| Tenant registry lives IN the shared DB | `tenants` table queried via `IgnoreQueryFilters()` in `TenantResolutionMiddleware.cs:206` | Needs a separate **catalog/management DB** holding the tenant→connection map |
| Provisioning inserts ROWS | `TenantProvisioningService.ProvisionAsync` inserts a `Tenant` + roles + shift; its own XML doc says isolation is "the EF global query filters + TenantInterceptor (RLS deferred)" | Must become **create DB + migrate + seed + register in catalog + rollback-on-failure** |
| Hangfire shares the app DB | `Program.cs:243` — `UsePostgreSqlStorage(... GetConnectionString("DefaultConnection"))`; 40+ recurring/enqueued jobs, many cross-tenant | Each job must carry a tenant→connection; shared vs per-tenant Hangfire is an open design question |
| DataProtection keys in the app DB | `DependencyInjection.cs:78-80` — `PersistKeysToDbContext<AppDbContext>()` (`data_protection_keys`) | Key ring must live in the shared/catalog DB, not scatter per tenant, or MFA-secret/cookie decryption breaks across tenants |
| EF second-level cache is tenant-prefixed | `Cache:SecondLevelCache` + tenant key prefix | Prefixing becomes moot per-DB, but the cache wiring still assumes one store |

### RLS: what is already in place (the alternative is 80% built)

- `Rls:Enabled` master switch (`appsettings.json:7-9`, default false).
- `ConnectionStrings:PrivilegedConnection` placeholder (`appsettings.json:4`, blank) for the
  `hrm_owner` (BYPASSRLS) connection.
- `TenantTransactionBehavior` (`Behaviors/TenantTransactionBehavior.cs`) — opens a per-request
  transaction and runs `set_config('app.current_tenant', <tenantId>, is_local => true)`;
  **no-op** unless `Rls:Enabled` is true, a non-system tenant is resolved, and the provider is
  relational.
- `roles.sql` — `hrm_app` (NOBYPASSRLS runtime) + `hrm_owner` (BYPASSRLS schema owner) roles.
- `Rls/README.md` — the documented Phase-4 switch-on plan (policies per tenant table, route
  system/admin/job paths to the privileged connection, CI test, flip the flag).

## The isolation-model spectrum (Postgres multi-tenancy)

Anchored to Microsoft's *Multitenant SaaS database tenancy patterns* and the Citus
*Design your SaaS database for scale* guidance.

| Model | Isolation | Scale ceiling | Per-tenant cost | Ops complexity | EF Core support |
|---|---|---|---|---|---|
| **(a) Shared DB + row discriminator (± RLS)** — *current* | Low→Medium (RLS raises it to strong logical) | Highest (1–1,000,000s) | Lowest | Low individually; sharding needed at extreme scale | Global query filter (first-class) |
| **(b) Shared DB + schema-per-tenant** | Medium | Low–Medium | Medium | Migrations × N schemas; `search_path` routing pitfalls | **Not supported / not recommended by EF Core** |
| **(c) Database-per-tenant** | High | High (with pooling/elastic tooling) | Higher (pools help) | Low–High (patterns tame it, but N-DB management) | "Configuration" — just a per-tenant connection string |
| **(d) Hybrid / sharded "tenant pools"** | Tunable per tenant | Effectively unlimited | Lowest for small tenants; high for isolated | Medium–High (a catalog + move/split/merge) | Discriminator + per-tenant connection (mix of a + c) |

Key upstream guidance:
- Citus: *"If you're building for **scale**: all tenants share the same table. If you're
  building for **isolation**: one database per tenant. Thousands of tenants → shard on
  `tenant_id`."*
- EF Core multi-tenancy doc: Discriminator = global query filter; Database-per-tenant =
  Configuration; **Schema-per-tenant = "not directly supported by EF Core and is not a
  recommended solution."** → option (b) is effectively off the table for this EF-migrations-driven
  codebase.
- Azure hybrid model: free-trial/small tenants pooled in a multitenant DB; premium/regulated
  tenants promoted to their own single-tenant DB, placed in elastic pools for cost efficiency —
  and *tenants can be moved between the two at any time.*

## Option analysis

### Option A — Finish RLS on the shared DB (the current trajectory)

**What it is:** additive hardening of the *existing* single database. Add RLS policies to the
109 tenant tables, run the app as the NOBYPASSRLS `hrm_app` role, set the `app.current_tenant`
GUC per request (already coded), and route migrations/seeding/system-context/cross-tenant jobs
to the BYPASSRLS `hrm_owner` connection.

**Pros:** No data-layer rewrite; strong *logical* isolation that survives app bugs (a stray
`IgnoreQueryFilters()`, raw SQL, an untenanted job); single DB to migrate/monitor/back up;
cross-tenant reporting stays a single query; 80% already scaffolded.

**Cons:** Still one physical database (a `DROP DATABASE`-style GDPR delete isn't available;
noisy-neighbor is only partially mitigated by resource governance); RLS adds a per-request
transaction; the known **`nohttp:` cache-prefix gap** must be closed before flip (see below).

### Option B — Database-per-tenant

**The EF/.NET pattern (feasible, documented):**
1. **Per-tenant connection resolved at request time.** Because `DbContext` pooling caches
   `OnConfiguring` (per EF Core *Advanced Performance Topics*), a per-tenant connection string
   requires either the **scoped-context-factory pattern** (`AddPooledDbContextFactory` + a
   scoped wrapper that injects tenant state — the `AspNetContextPoolingWithState` sample) or an
   `IDbConnectionInterceptor.ConnectionOpeningAsync` that swaps the connection string per
   tenant. Today's plain `AddDbContext(... DefaultConnection)` supports neither.
2. **A catalog/management DB** holding the tenant registry + tenant→connection map (moves the
   `tenants` table out of the per-tenant DBs).
3. **Migration-per-tenant orchestration** — apply the 109-table schema across every tenant DB
   on release, with drift detection.
4. **Automated provisioning on onboarding:** create DB → `MigrateAsync` → seed roles/admin/
   master data → register in catalog → **rollback (drop the half-built DB) on any failure**.
   The existing subdomain→catalog resolution seam (`TenantResolutionMiddleware`) already gives
   us the request-time tenant key; provisioning just gains a "create + migrate + seed" step in
   front of today's row-insert.

**Latency/failure modes:** `CREATE DATABASE` + 109-table migration + seed is seconds-to-minutes
of onboarding latency (vs today's sub-second row insert) → provisioning must be async/queued
with a visible "provisioning" state and idempotent retry; a partial failure must be rolled back
or the tenant is stuck half-created.

**Pros:** Strong **physical** isolation (leak requires a wrong connection string, not a wrong
`WHERE` — eliminates the BUG-003 cross-tenant class this repo has hit); per-tenant
backup/restore/PITR; **GDPR delete = `DROP DATABASE`**; noisy-neighbor isolation; per-tenant
data residency/compliance; per-tenant scaling/customization; RLS + query filters become
*redundant* as the isolation mechanism (kept only as cheap defense-in-depth).

**Cons:**
- **Full data-layer rewrite** + permanent ops burden (migrations across N DBs + drift risk).
- **Connection-pool explosion.** A PostgreSQL connection is a separate OS process (~5–10 MB
  RAM); `max_connections` is the wall SaaS backends hit first. Many tenant DBs multiply the
  per-(user,database) pools → **PgBouncer becomes mandatory** (it multiplexes thousands of
  client connections onto tens of server connections, 10–25×). None exists today.
- Cross-tenant analytics/platform-monitoring (US-ADM-002) and billing stop being one query →
  need **ETL/a warehouse**.
- Cost of many managed DBs; **connection-secret sprawl** (N connection strings to store in a
  vault, not one); monitoring N databases; Hangfire + DataProtection re-architecture.
- Single-Postgres-server practicality ceiling: a few hundred tenant DBs before you *need*
  sharding/elastic pools (Citus, Azure Elastic Pools, AWS Aurora).

### Option C — Hybrid (pooled + RLS for most; dedicated DB for premium/regulated)

Azure's recommended middle path: the bulk of tenants stay in the shared RLS database; specific
premium/regulated/data-residency tenants are promoted to a dedicated database (or instance).
The `tenants` catalog gains a nullable `ConnectionString` (or `ShardKey`) column — null = shared
RLS DB, set = dedicated DB. Resolution stays subdomain-driven. This is the **only** option that
lets us evolve *incrementally* from where we are without a big-bang rewrite.

## Infrastructure suitability (honest assessment of our stack)

Current: one Postgres (native PG18 dev + Docker), Hangfire on that same Postgres, Redis
(SignalR backplane + IDistributedCache), **no PgBouncer**, **no per-tenant provisioning
pipeline**, **no catalog DB**, **no managed elastic pool**, **no per-tenant secret management**.

| Tier | DB-per-tenant supported today? | Investment required |
|---|---|---|
| Small (≤ ~50 tenants) | Barely — mechanically possible, operationally raw | Provisioning pipeline + catalog DB + scoped context factory + per-tenant migration runner |
| Medium (100s) | No | + PgBouncer (mandatory), secret vault for N connection strings, per-tenant Hangfire strategy, N-DB monitoring |
| Large (1000s+) | No | + sharding / elastic pools (Citus or a managed elastic-pool service), a split/merge + rebalance tooling, cross-tenant ETL/warehouse |

RLS, by contrast, runs on the infrastructure we already have — one Postgres, one Hangfire, one
Redis — and only needs the `hrm_owner`/`hrm_app` roles from `roles.sql` created once by a DBA.

## Effort comparison

- **Finish RLS ≈ M.** Additive on the existing DB, ~80% scaffolded. Remaining: one
  `Platform_RowLevelSecurity` migration adding policies to the 109 tenant tables; route the
  ~40 Hangfire jobs + `DbInitializer` seeding + the tenant-lookup + system/admin paths to
  `PrivilegedConnection`; fix the `nohttp:` cache-prefix (AsyncLocal ambient tenant); a CI RLS
  isolation test against the postgres service container; flip `Rls:Enabled`.
- **DB-per-tenant ≈ XL.** Structural rewrite of the data layer + a new provisioning subsystem +
  new infra (PgBouncer, catalog DB, secret management) + ongoing N-DB ops. Roughly a **4–6×**
  effort multiple over RLS, plus a *recurring* operational cost RLS never incurs.

## Decision (recommended)

1. **Adopt shared-DB + row-discriminator + RLS as the platform default. Finish the in-flight
   RLS work (US-PLT-002 Phase 4).** It is the standard default for a horizontally-scalable HRM
   SaaS, fits our current infra, and is mostly built. **Confidence: High (~85%).**
2. **Do NOT build database-per-tenant speculatively.** Reserve it for a concrete trigger:
   a contractual data-residency requirement, a regulated/enterprise tenant demanding physical
   isolation, or a demonstrated noisy-neighbor problem RLS + resource governance can't solve.
3. **Design the RLS work so the hybrid path stays open.** Keep tenant resolution catalog-driven
   (it already is) and treat a future nullable per-tenant `ConnectionString` on the `tenants`
   registry as the promotion seam. This lets a premium tenant move to a dedicated DB later
   without re-architecting everyone else.

## What it would take (if/when DB-per-tenant is triggered)

- Introduce a **catalog/management DB** + move the `tenants` registry into it.
- Replace `AddDbContext(... DefaultConnection)` with a **scoped pooled-context factory** (or an
  `IDbConnectionInterceptor`) that resolves the per-tenant connection at request time.
- Build the **provisioning pipeline** (create DB → migrate → seed → register → rollback), async
  + idempotent, fronted by the existing subdomain seam.
- Build a **per-tenant migration runner** (apply schema across N DBs; detect drift).
- Decide **Hangfire** topology (shared queue with tenant→connection carried on each job, vs
  per-tenant storage) and **DataProtection** key location (shared/catalog DB).
- Stand up **PgBouncer**, a **secret vault** for N connection strings, **N-DB monitoring**, and
  a **cross-tenant ETL/warehouse** for platform analytics.
- Above a few hundred tenants, adopt **sharding/elastic pools** (Citus / managed elastic pool).

## Sources

- Azure — Multitenant SaaS database tenancy patterns:
  https://learn.microsoft.com/azure/azure-sql/database/saas-tenancy-app-design-patterns
- EF Core — Multi-tenancy:
  https://learn.microsoft.com/ef/core/miscellaneous/multitenancy
- EF Core — Advanced Performance Topics (DbContext pooling with per-request state):
  https://learn.microsoft.com/ef/core/performance/advanced-performance-topics
- EF Core — Global Query Filters (multi-tenancy):
  https://learn.microsoft.com/ef/core/querying/filters
- EF Core — Interceptors (per-tenant connection via IDbConnectionInterceptor):
  https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors
- Citus / Postgres — Designing your SaaS database for scale:
  https://learn.microsoft.com/postgresql/citus/designing-saas
- PgBouncer connection pooling at SaaS scale:
  https://www.pgbouncer.org/config.html
- Internal: `src/backend/HRM.Infrastructure/Persistence/Rls/README.md`,
  `user-stories/platform/US-PLT-002.md`, `[[ADR-2026-07-08-saas-data-governance-posture]]`
