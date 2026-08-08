# Pass E — architecture conformance (§8 / §9 / §10)

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains` @ `923db177`
> **Question:** does the system as built match the architecture as documented?
> **Status:** ✅ VALIDATED — 5 of 5 orchestrator spot-checks confirmed.
> **Headline judgement:** the isolation architecture is **real and better than average**. What has drifted is the **document** — plus a handful of narrow edges the three-layer model does not actually cover.

## Orchestrator validation

| Claim | Result |
|---|---|
| `PayrollReportExport` + `TenantLatencyBucket` have no global query filter | ✅ **Confirmed.** Both exposed as `DbSet`s (`AppDbContext.cs:174`, `:237`), deriving from `BaseEntity`; `grep HasQueryFilter` for either returns nothing. |
| A code comment asserts a filter that does not exist | ✅ **Confirmed.** `PayrollReportExportService.cs:307`: *"The global query filter scopes this to the caller's tenant — a cross-tenant id simply isn't found."* It does not. The actual protection is `export.RequestedByUserId != userId` at `:313-315`. |
| No Hangfire `IServerFilter` anywhere | ✅ **Confirmed.** `IServerFilter`/`GlobalJobFilters`/`IApplyStateFilter`/`JobFilterAttribute` → zero non-test hits. |
| Rate limiting has no tenant/user dimension | ✅ **Confirmed.** All three policies partition on `ResolveClientIp(httpContext)` (`Program.cs:530,544,559`). No `GlobalLimiter`. |
| Outbound webhooks do not exist | ✅ **Confirmed.** Zero hits for `webhook` in `src/backend`. |

### The auditor's pushback on the brief (accepted)

The brief asked it to *"enumerate every `IgnoreQueryFilters()` call site and judge each."* There are **662 occurrences repo-wide (~230 in production code across ~60 files)** — judging each would consume the pass and produce noise. It audited by **pattern class** and spot-verified each class's predicate instead, and said so explicitly so the result is not misread as "all 662 individually cleared." Correct call.

It also declined to inflate one item: `§10.1 tests/HRM.TenantIsolationTests` is **naming drift, not a missing suite** — the isolation tests exist inside `HRM.Tests/Integration/` (`RlsIsolationPostgresTests.cs`, `TenantGucInterceptorRlsPostgresTests.cs`, …). `HRM.ArchitectureTests`, by contrast, genuinely does not exist.

---

## VERDICT TABLE (condensed — full evidence retained)

### §8 — Architecture & pipeline

| Req | Requirement | Verdict | Evidence |
|---|---|---|---|
| §8.1-1 | Angular SPA static; one build all tenants; runtime branding | IMPLEMENTED | `frontend/nginx.conf`; `TenantContextController.cs`; `tenant.service.ts:268-310` — only `--brand-primary` actually applied (`:374-377`) |
| §8.1-2 | Wildcard DNS → same LB; host-header resolution | IMPLEMENTED (dev) / UNVERIFIABLE (prod) | `local-dev/nginx.docker.conf:26`; `TenantResolutionMiddleware.cs:70-73` |
| §8.1-3 | API stateless, horizontally scalable | PARTIAL | `Program.cs:514-515` — rate limiter explicitly in-process ("not a distributed quota"), so limits are per-instance under scale-out |
| §8.1-4 | EF filters **+ RLS** enforce isolation | **PARTIAL** | `AppDbContext.cs:268-270` (132 filters); **`:174` and `:237` have none** → GAP #1 |
| §8.1-5 | Redis caches + SignalR backplane | IMPLEMENTED | `DependencyInjection.cs:928`; `Program.cs:238` |
| §8.1-6 | Hangfire workers **tenant-aware** | **PARTIAL** | `TenantJobRunner.cs:40,60-62`; **no `IServerFilter`** → GAP #2 |
| §8.1-7 | SignalR tenant/user-scoped groups | IMPLEMENTED | `NotificationHub.cs:44,47,64` — `t:{tid}:user:{uid}`, strictly stronger than documented |
| §8.1-8 | Blob storage, tenant-scoped paths | PARTIAL | `LocalFileStorage.cs:95-96,102-112` tenant-partitioned ✓, but backing store is **local disk / OS temp**, not blob; `LocalReportExportStorage` lacks the traversal-escape assertion |
| §8.2-1 | `ExceptionHandlingMiddleware` outermost | IMPLEMENTED | `Program.cs:586` |
| §8.2-2 | **Tenant resolution before authentication** | IMPLEMENTED | `Program.cs:589` → `:592`. Documented order holds exactly |
| §8.2-3 | Subdomain → tenant_id (Redis, DB fallback) | IMPLEMENTED | `TenantResolutionMiddleware.cs:230-318` |
| §8.2-4 | Reject unknown / suspended / terminated at resolution | **PARTIAL** | unknown 404 `:121-126`; suspended/terminating `TenantStatusEnforcementMiddleware.cs:86-116`; **`Terminated` not handled** → GAP #4 |
| §8.2-5 | Auth verifies `tenant_id` claim matches resolved tenant | IMPLEMENTED | `TenantAccessGuardMiddleware.cs:36-56`; test-bound `RefreshTokenCrossTenantRejectTests.cs` |
| §8.2-6 | EF auto-scoped; RLS as second line | PARTIAL | RLS on-by-default `appsettings.json:20-22`, **fail-closed at startup** `DependencyInjection.cs:60-69`; but `appsettings.Development.json:8-10` sets `Rls:Enabled=false` — **dev runs on ONE layer** |
| §8.3-1 | Dependency direction Api→Application→Domain | IMPLEMENTED | `HRM.Api.csproj:72-73`; `HRM.Application.csproj:19`; `HRM.Infrastructure.csproj:87` — **no violation** |
| §8.3-2 | `HRM.Domain` has no framework dependencies | IMPLEMENTED | zero `PackageReference`. **Claim verified true** |
| §8.3-3 | Domain holds **Domain Events · Specifications** | **MISSING** | `IDomainEvent`/`DomainEvent` → 0 hits; no `Specification` type. Neither pattern exists |
| §8.4-1 | Standalone components by feature | IMPLEMENTED | 221 `standalone: true`, zero `@NgModule` |
| §8.4-2 | Shared module for UI primitives, pipes, directives | PARTIAL | `shared/` — 1 component, 3 directives, **0 pipes** |
| §8.4-3 | Core: auth, tenant, **http, error handler, notification, logging** | **PARTIAL** | 2 of 6 present as named. No `core/http/`, no `ErrorHandler` provider, no `core/notification/`, no logging service. Notifications: raw `ngx-toastr` injected ~288 places |
| §8.4-4 | Lazy loading per feature route | IMPLEMENTED | 34 `loadChildren` + 25 `loadComponent`; only eager refs are the 2 layout shells |
| §8.4-5 | Bootstrap: tenant → branding + modules → shell | IMPLEMENTED | `app.config.ts:44-56,114-119` — all 3, blocking. Drift: legacy `APP_INITIALIZER` on Angular 20, not `provideAppInitializer` |

### §9 — Multi-tenancy

| Req | Requirement | Verdict | Evidence |
|---|---|---|---|
| §9.1-1 | Shared DB/schema + `tenant_id` discriminator | IMPLEMENTED | `BaseEntity.cs:10`; 126 entities derive |
| §9.1-2 | Layer 1 — every request carries `ITenantContext` | IMPLEMENTED | `TenantResolutionMiddleware.cs:148-159` |
| §9.1-3 | Layer 2 — filters scope **all** queries | **PARTIAL** | 130 filtered; **2 missing**; filter is `!IsResolved \|\| …` i.e. **fail-open when unresolved** → GAP #1 |
| §9.1-4 | Layer 3 — RLS on every business table | IMPLEMENTED (config-gated) | `20260710120000_Platform_RlsPolicies_Dormant.cs:45,65,77` (PL/pgSQL auto-discovery); reconciler `DbInitializer.cs:101,158`; startup guard. **Dev default OFF** |
| §9.2-1 | Subdomain-primary resolution | IMPLEMENTED | `TenantResolutionMiddleware.cs:177-200` |
| §9.2-2 | 16 reserved subdomains cannot be claimed | IMPLEMENTED | `:51-54` — **exact same 16, same order**; enforced at creation `TenantProvisioningService.cs:88,303` |
| §9.2-3 | `admin.*` → system context granting cross-tenant ops | 🔴 **CONTRADICTED** | see C-1 |
| §9.2-4 | Dev `X-Tenant-Subdomain` fallback (dev-only) | IMPLEMENTED | `:25,78-87` gated on `IsDevelopment()`; spoof closed by `TenantAccessGuardMiddleware` |
| §9.2-5 | Custom domains deferred to Phase 2 | IMPLEMENTED (seam) | `:128-145,205-221` plan-gated, inert in prod — ahead of the doc |
| §9.3-1 | Global `users`, email unique platform-wide | IMPLEMENTED | `UserConfiguration.cs:31-32` — no tenant in the key |
| §9.3-2 | `user_tenants` junction | IMPLEMENTED | `UserTenant.cs:10-19` |
| §9.3-3 | Switch memberships without re-auth, re-mint JWT | IMPLEMENTED | `AuthService.cs:1791`; test-bound `AuthTenantSwitchTests.cs` |
| §9.3-4 | Roles per-membership, not per-user | IMPLEMENTED | `AuthService.cs:312-319` |
| §9.4-1 | `ITenantContext` scoped, documented shape | IMPLEMENTED | `ITenantContext.cs:9-49`. Drift: `SubscriptionPlan Plan` is `string? Plan`; adds `IsResolved`, `LogoUrl`, `PrimaryColor`, `FeatureFlags` — additive |
| §9.4-2 | EF interceptor sets PG session var for RLS | IMPLEMENTED | `TenantGucConnectionInterceptor.cs:46`. **Naming drift:** GUC is `app.current_tenant`, doc says `app.current_tenant_id`. Internally consistent |
| §9.4-3 | Jobs: `TenantId` required; **Hangfire filter** restores context | **PARTIAL** | `TenantJobRunner.cs:40,60-62`; **no `IServerFilter`** → GAP #2 |
| §9.4-4 | SignalR user+tenant groups from claim | IMPLEMENTED | `NotificationHub.cs:44,47,60,64,83-88` — never client input |
| §9.4-5 | Serilog enricher adds `tenant_id` to **every** record | PARTIAL | `TenantResolutionMiddleware.cs:168-171` is the **only** `PushProperty` site — **background-job logs carry no `tenant_id`** |
| §9.4-6 | Cache keys prefixed `t:{tenantId}:` | IMPLEMENTED (drift) | `CacheTenantPrefix.cs:55,82`; EF 2nd-level cache `:114`; `AmbientTenantCacheKeyProvider.cs:20`. **The high-risk item — the EF query cache — is correctly partitioned and derives the prefix from `ITenantContext`, not SQL text, so it stays right under RLS** |
| §9.4-7 | Webhooks/emails from per-tenant templates, tenant-signed | PARTIAL | templates ✓ `NotificationTemplateService.cs:49,123`; **webhooks: 0 hits** |
| §9.5-1 | All six states modelled | IMPLEMENTED | `Tenant.cs:326-334` |
| §9.5-2 | `trial` — full access, `trial_ends_at` set | PARTIAL | `Tenant.cs:102` written at provisioning; **no trial-expiry job, no Trial→Active/PastDue transition** — never acted on |
| §9.5-3 | `active` — full | IMPLEMENTED | `TenantStatusEnforcementMiddleware.cs:68` |
| §9.5-4 | `past_due` — read-only after grace + reminders | **MISSING** | **`Status = TenantStatus.PastDue` → 0 assignments in prod code.** State unreachable *and* unenforced → GAP #3 |
| §9.5-5 | `suspended` — blocked, tenant admin only, reason shown | IMPLEMENTED | `:86-101` (451, admin/owner exempt); test-bound `TenantLifecycleIntegrationTests.cs` |
| §9.5-6 | `terminating` — read-only, export-only, 30d grace | IMPLEMENTED | `:104-116,44`; ISSUE-217 regression already closed at `:44` |
| §9.5-7 | `terminated` — login **and API** blocked | **PARTIAL** | login blocked 3 places; **API not** — `:68` lets `Terminated` fall through → GAP #4 |
| §9.6-1 | 4-level config: User → Tenant → Plan → System | PARTIAL | Plan→Tenant chain correct (`EmployeeService.cs:1121-1136` + `PlanLimitOverride.cs`). **No user level for language/timezone**; no unified resolver — precedence re-implemented per concern. *85%* |
| §9.6-2 | Plan owns numerical limits; Tenant owns branding/policies | IMPLEMENTED | `SubscriptionPlan.cs`; `Tenant.cs:210-232` snapshot correctly treated as last-resort fallback |
| §9.7-3 | File storage path prefix `{tenantId}/…` | IMPLEMENTED | `LocalFileStorage.cs:95-96` + escape assertion `:102-112`; **`tenantId` mandatory on every `IFileStorage` method — cannot be forgotten at a call site** |
| §9.7-4 | Jobs — `TenantId` param + filter | **PARTIAL** | 3 jobs establish no context: `HealthProbeRecorderJob.cs:42-46`, `EncryptionKeyAgeWatchdogJob.cs:42-44,59,64`, flat cross-tenant loop `DocumentExpiryNotificationJob.cs:51-80` → GAP #2 |
| §9.7-6 | Logs / **metrics** / **traces** carry `tenant_id` | PARTIAL | logs ✓; Sentry ✓ `TenantTagSentryEventProcessor.cs:29`; **OTel spans ✗** — `ObservabilityExtensions.cs:40` says verbatim *"per-tenant enrichment … deferred to a later slice"*. Request spans do carry it (`TenantResolutionMiddleware.cs:164-165`); downstream/job spans do not |
| §9.7-7 | Outbound webhooks — per-tenant URLs + signing keys | **MISSING** | 0 matches repo-wide → GAP #6 |
| §9.7-8 | Audit log `tenant_id`; **system audit separate** | PARTIAL (documented deviation) | `AuditLog.cs:12`: *"There is intentionally no second audit table."* A **knowing decision**; the doc never absorbed it → C-3 |
| §9.7-9 | Rate limiting per-tenant **and** per-user; sysadmin exempt | **MISSING** | `Program.cs:521-566` — all 3 policies partition by **client IP only**; no `GlobalLimiter`, no exemption; all 3 attached to anonymous endpoints only → GAP #5 |
| §9.8-1 | System tenant `is_system = true`, reserved id | PARTIAL | **No `IsSystem` property**; identified by config subdomain (`Platform:SystemTenantSubdomain ?? "platform"`). Meets intent under a different mechanism; string-compare-per-call-site is fragile |
| §9.8-2 | Hosts system admins; owns cross-tenant data | IMPLEMENTED | `DbInitializer.cs:435-437` |
| §9.8-3 | Accessible **only via `admin.yourhrm.com`** | 🔴 **CONTRADICTED** | see C-1 |
| §9.8-4 | Cannot be suspended/terminated normally | IMPLEMENTED | `TenantLifecycleService.cs:369-371` (`system_tenant_protected`) |
| §9.9-1 | Cross-tenant ops only in system context | IMPLEMENTED | `SystemEndpointHostGuardMiddleware.cs:39` confines `/api/v1/system/*` (7 of 80 controllers); elevation ≠ authorization — `[RequirePermission]` still applies |
| §9.9-2 | Cross-tenant reports aggregated, **no PII** | IMPLEMENTED | `PlatformMonitoringService.cs` — 12 `IgnoreQueryFilters` sites, all aggregate/group-by |
| §9.9-3 | Impersonation logged + tenant admin notified | IMPLEMENTED | `ImpersonationService.cs:159,245,190-200`; blocks Terminated/Terminating `:74`, blocks system users `:78-80,106` |
| §9.9-4 | **Every** cross-tenant path annotated, audited, tested | PARTIAL | strong where it counts; ~230 prod sites **not** uniformly annotated. *75%* |
| §10.1 / §10.2 | Documented folder layouts | PARTIAL (structural) | → GAP #7 |

---

## CONTRADICTIONS

### 🔴 C-1 — `admin.yourhrm.com` is documented as the system-admin entry point. It is not, and cannot be.

> **§9.2 (~:530):** *"For system admin: separate subdomain `admin.yourhrm.com` resolves to a 'system tenant' context that grants cross-tenant operations to authorized users."*
> **§9.8 (~:645):** *"Accessible only via `admin.yourhrm.com`."*

The implementation's own words, `SystemEndpointHostGuardMiddleware.cs:28-32`:

> *"Platform administrators are not on `admin.*` — they are users of a real tenant whose subdomain is `platform` (DbInitializer's default admin tenant). `admin.*` resolves to system context with `TenantId == Guid.Empty`, which **no `user_tenants` row can match, so nobody can log in there at all.** A guard that demanded `IsSystemContext` would therefore 403 the entire platform console."*

Corroborated: `DbInitializer.cs:21` `DefaultTenantSubdomain = "platform"`; `TenantResolutionMiddleware.cs:54` puts `admin` in the reserved list so it can never be a tenant; `ImpersonationService.cs:348` and `TenantLifecycleService.cs:305,369` key off `Platform:SystemTenantSubdomain ?? "platform"`.

**Verdict:** the *capability* is fully implemented and arguably better than documented. The **documented access mechanism is false** — anyone provisioning production from §9.2/§9.8 would point DNS and the console at a subdomain nobody can authenticate on. Doc-drift, not a code defect. **Fix the doc.**

### 🔴 C-2 — A code comment asserts a global query filter that does not exist

`PayrollReportExportService.cs:307` (orchestrator-verified):
```csharp
// The global query filter scopes this to the caller's tenant — a cross-tenant id simply isn't found.
var export = await _db.PayrollReportExports.FirstOrDefaultAsync(e => e.Id == exportId, ct);
```
`PayrollReportExport : BaseEntity` is exposed at `AppDbContext.cs:174` with **no** `HasQueryFilter`, in `OnModelCreating` or in its configuration class. **The comment is wrong.**

What actually holds the line is the *next* check — `export.RequestedByUserId != userId` (`:313-315`). Sufficient for a single-tenant user — but §9.3 explicitly ships **global users with multi-tenant memberships**, so a user who is a member of tenants A and B can, while operating in B, download an export they generated in A. Same table, **no owner check at all**, at `:194-195` (`GenerateAsync`) and `:101` (concurrency count).

### C-3 — Reverse drift: "system audit in a separate log" was consciously overruled, and the code says so

`AuditLog.cs:12` — *"There is intentionally no second audit table."* — and `IAuditLogPurgeService.cs:6` — *"FR-6 — 'purge logged in `system_audit_log`'; this platform reuses the single audit table with a system action."* System rows carry `TenantId = null`. **A decision, not a defect.** The tech doc never absorbed it. **Do not book it as work.**

---

## GAPS RANKED

Ranked by isolation-impact × blast radius, per the calibration rule.

**#1 — Two tenant-scoped entities have no global query filter.** `PayrollReportExport` (`AppDbContext.cs:174`) and `TenantLatencyBucket` (`:237`) derive from `BaseEntity`, are exposed as `DbSet`s, and have no filter anywhere. **Layer 2 of the documented three-layer model is simply absent for them.** `TenantLatencyBucket` is only read via explicit `IgnoreQueryFilters()` in system-context monitoring — benign. `PayrollReportExport` **is reachable from a tenant request path** (C-2). *Mitigation:* RLS covers both when `Rls:Enabled=true` (the DO-block reconciler discovers tables by `tenant_id` column) — but the **Development default is `false`**, so dev/CI runs on one layer. **Fix:** add the two `HasQueryFilter` lines matching the adjacent pattern; delete the false comment. **Size: S**

**#2 — The documented Hangfire tenant filter does not exist; three jobs run with no tenant context.** No `IServerFilter` anywhere. The real mechanism is `ITenantJobRunner.RunForTenantAsync` applied **per job, by hand** — and applied well (~17 jobs take `tenantId` as a required param, ~21 enumerate tenants, ~10 are deliberate system jobs). But three touch the DB with an unresolved ambient tenant: `HealthProbeRecorderJob.cs:42-46`, `EncryptionKeyAgeWatchdogJob.cs:42-44,59,64` (neither calls `SetSystemContext()`), and `DocumentExpiryNotificationJob.cs:51-80`, which scans **all** tenants' documents and dispatches notifications in a flat loop while `ITenantContext` stays system-scoped — correctness rests entirely on the downstream service honouring the passed `doc.TenantId` over the ambient one. Compounding: **no background-job log line carries `tenant_id`**, which is exactly where you would need it to diagnose an isolation incident. **Fix:** a real `IServerFilter` reading a `tenantId` job argument and populating `ITenantContext` + log context; short of that, `SetSystemContext()` on the three and wrap the document loop in `RunForTenantAsync`. **Size: M / S**

**#3 — `past_due` is documented behaviour that is unreachable and unenforced.** The enum member exists and the export gate references it, but **nothing assigns it** and the enforcement middleware handles only `Suspended`/`Terminating`. Adjacent: `Tenant.TrialEndsAt` is written at provisioning and **never read by any job** — no trial expiry, no Trial→Active conversion. **Honest read:** billing is offline in Phase 1 (`SubscriptionPlan.cs:28` says so), so this is a **deferral, not a defect** — but the doc presents both as live behaviour. **Fix:** mark Phase 2 in §9.5 now; build when billing lands. **Size: S (doc) / M (build)**

**#4 — `terminated` tenants are not blocked at the API layer.** Login blocked in three places, but `TenantStatusEnforcementMiddleware.cs:68` returns early for any status other than `Suspended`/`Terminating`, so a JWT minted before termination keeps working until it expires. Window is one access-token lifetime and the data is being purged anyway — **low severity, trivially closed.** **Fix:** add `TenantStatus.Terminated` to the guard. **Size: S**

**#5 — Rate limiting has no tenant or user dimension.** Three policies, all partitioned on client IP, all attached to **anonymous endpoints only**. No `GlobalLimiter`, no per-tenant quota, no per-user policy, no system-admin exemption. **Every authenticated tenant endpoint is unthrottled** — one tenant can consume the shared instance's capacity. A noisy-neighbour and cost problem in a shared-schema SaaS, **not** a data-isolation breach. Compounded by the limiter being in-process, so limits don't hold under scale-out. **Fix:** global limiter partitioned on `(tenantId, userId)` with a system bypass; Redis-backed when multi-instance. **Size: M**

**#6 — Outbound webhooks do not exist.** Zero hits. A whole documented subsystem with a documented tenant-isolation contract (per-tenant URLs + signing keys) is unbuilt. No evidence anyone is waiting on it. **Fix:** mark Phase 2. **Size: S (doc) / L (build)**

**#7 — Documented folder layouts are stale in both projects.** Ranked last per calibration. Backend absences beyond naming: `HRM.Shared`, `Domain/Events` (no domain-event pattern at all — §8.3-3), `Api/Extensions/` (DI lives in a 959-line `Program.cs`), `Application/DependencyInjection.cs`, `tests/HRM.ArchitectureTests`. Frontend: `core/http/`, `core/notification/`, `features/billing/`, `features/self-service/` absent entirely; `tailwind.config.ts` is actually `tailwind.config.js` (**Tailwind v3**); `src/i18n/` → `src/assets/i18n/`. Conversely ~11 real feature areas are undocumented. **Fix:** regenerate both trees from `find`. **Size: S.** **The one item here worth *building* rather than documenting is `HRM.ArchitectureTests` — it would have mechanically caught gap #1.**

---

## COVERAGE SUMMARY

```
Requirements audited: 57 | IMPLEMENTED: 30 | PARTIAL: 20 | MISSING: 3 | CONTRADICTED: 2 (+1 reverse drift)
```

**Where the failures concentrate: overwhelmingly leg 1 (code does not do the documented thing), not leg 2 or 3.** This is a well-wired codebase whose *document* has drifted — not a codebase of orphaned parts. Two distinct clusters:

- **The doc over-promises subsystems that were consciously deferred** — webhooks, per-tenant SMTP, per-tenant rate limits, PastDue billing, OTel per-tenant enrichment, blob storage, domain events. Every one is Phase-2-shaped and several are annotated as deferred *in the code*. **Documentation debt, not engineering debt.**
- **The genuine engineering gaps are narrow and concentrated at the boundary between the three isolation layers** — 2 entities missing an EF filter, 3 jobs missing a tenant context, 1 lifecycle state unenforced. **All S-sized.**

**Headline judgement.** The isolation architecture is real and better than average: three independent layers, a fail-closed RLS startup guard, a spoof-proof cross-tenant JWT guard, a `tenantId`-mandatory storage interface that cannot be forgotten at a call site, and — the single smartest thing in this codebase — a cache-key provider that derives the tenant from `ITenantContext` rather than SQL text, so it **stays correct after the RLS flip moves `tenant_id` out of the SQL.** The documented pipeline order holds exactly. The documented dependency direction holds exactly, with a genuinely dependency-free Domain.

---

## CONFIDENCE

**Overall: 88%.** §8/§9/§10 are structural claims — the friendliest possible target for static reading.

- **§9.6-1 (config hierarchy) — 85%.** Plan→Tenant chain verified concretely; "no unified 4-level resolver" is an absence claim across a large surface. Searched `ResolveLanguage|EffectiveLanguage|LanguageResolver|ResolveLimit|EffectiveLimit`. A resolver under untried naming would move this to IMPLEMENTED.
- **§9.9-4 — 75%.** Auth hot-path predicates verified line by line; ~230 production `IgnoreQueryFilters()` sites **not** all read. *Settled by:* an `HRM.ArchitectureTests` rule requiring every `IgnoreQueryFilters()` to sit in a method carrying an explicit `[CrossTenant]` attribute. **That test would also have caught gap #1 — the highest-leverage single item in this report.**
- **§8.1-2 production leg — UNVERIFIABLE by design.**
- **C-2 exploitability — 80%** that impact is limited to a multi-tenant user reading their *own* prior-tenant export. *Settled by:* a running-stack probe (`@test-runner` work).
- **§9.7 rows — 90%**, resting on a delegated sub-audit whose two load-bearing claims were independently re-verified.

---

## OUT-OF-LANE

- **type:** risk · **severity:** MED · **where:** `AppDbContext.cs:270` (and all 130 filter definitions) · **what:** every filter is written `!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId` — when no tenant is resolved the filter is a **no-op returning all tenants' rows**, rather than denying. · **why-out-of-lane:** the doc does not specify unresolved-context behaviour, so this is neither conformant nor non-conformant — an undocumented design decision. · **suggested-action:** decide and document the intent. It is load-bearing for the three jobs in gap #2 that run unresolved, and it is why `SystemEndpointHostGuardMiddleware.cs:12-14` says a system endpoint reached on a tenant host "still works today". Under `Rls:Enabled=true` the DB catches it; under the Development default (`false`) nothing does.
- **type:** risk · **severity:** MED · **where:** `appsettings.Development.json:8-10` · **what:** `Rls:Enabled=false` in Development means every local run and every non-Postgres test exercises **two** of the three documented isolation layers, never three. · **why-out-of-lane:** environment configuration, not architecture conformance — the committed production default is correctly `true` + fail-closed. · **suggested-action:** already tracked (`appsettings.json:3` documents the planned dev repoint to `hrm_app`/`hrm_owner` after `roles.sql`). Confirm it is on the plan.
- **type:** doc-drift · **severity:** LOW · **where:** `TenantSettingsService.cs:496` · **what:** evicts cache key `t:{tenantId}:config`, which no code writes or reads — the FR-7 eviction is a no-op against a never-populated entry. · **suggested-action:** wire the config cache or delete the eviction.
- **type:** risk · **severity:** LOW · **where:** `TenantResolutionMiddleware.cs:26` and `TenantResolutionCache.cs:21` · **what:** the cache-key prefix `t:subdomain:` is duplicated as a constant in two files, held together by a "MUST stay in sync" comment rather than a shared constant. · **suggested-action:** promote to one shared constant — a silent divergence would break cache invalidation on subdomain change.
