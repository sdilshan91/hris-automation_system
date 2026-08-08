# Pass C — non-functional requirements (§6.1–§6.12)

> **Run:** 2026-08-08 · **Tree:** `test/local-subdomains`
> **44 documented requirement bullets**, one verdict row each.
> **Status:** ✅ VALIDATED — 6 of 6 orchestrator spot-checks confirmed, including the full fail-open chain.
> **Headline:** 🔴 **an unresolved tenant context disengages ALL THREE isolation mechanisms simultaneously** — and the reserved-subdomain list makes the most conventional API deployment layout the trigger.

## Orchestrator validation — the fail-open chain, verified link by link

Every step re-read directly. **All confirmed.**

| Link | Claim | Verified evidence |
|---|---|---|
| 1 | No subdomain ⇒ request proceeds unresolved | `TenantResolutionMiddleware.cs:90-93` — `// No subdomain (e.g., yourhrm.com or localhost)` → `await _next(context); return;` |
| 2 | Reserved subdomains include `api` and `app` | `TenantResolutionMiddleware.cs:54` — default list is `["www", "api", "admin", "app", "mail", "status", …]`. **Serving the API at `api.<domain>` means every request resolves to nothing.** |
| 3 | EF filter becomes a tautology when unresolved | `AppDbContext.cs:270` onward — every filter is `!_tenantContext.IsResolved \|\| x.TenantId == …`. Unresolved ⇒ **no row excluded** *(also independently found by Pass E)* |
| 4 | Unresolved routes to the privileged connection | `ConnectionRoutingInterceptor.cs:92-93` — `SelectPrivileged() => AmbientTenant.Current is not { IsResolved: true, IsSystemContext: false }`. Its own docstring: *"unresolved / null / system ⇒ privileged"* |
| 5 | The privileged role bypasses RLS | `Rls/roles.sql:18` — *"`hrm_owner` — the PRIVILEGED role … and has **BYPASSRLS**"*; created with `BYPASSRLS` at `:38`. (`hrm_app` is correctly `NOBYPASSRLS` at `:35`.) |
| 6 | The cross-tenant guard skips unresolved requests | `TenantAccessGuardMiddleware.cs:38-41` — enforcement requires `tenantContext.IsResolved`; unresolved falls straight through |

**Composed effect:** for a request with no resolvable subdomain, the EF query filter returns every tenant's rows, the database connection bypasses RLS entirely, and the BUG-003 cross-tenant guard declines to act. **All three layers of the documented "multiple layers" isolation model are off at once.**

**Reachability:** today's rig uses per-tenant subdomains, so this is **latent, not live**. It becomes live the moment the API is served from `api.<basedomain>`, `app.<basedomain>`, or the apex domain — the most conventional API layouts, and two of them are in the shipped reserved list.

**Also confirmed — C-1, the audit-retention contradiction:**
`hrm_technical_document_v4.0.md:369` states verbatim *"GDPR-aligned: data subject access & erasure (per tenant, per user), **7-year audit retention**."*
`AuditLogPurgeService.cs:41` — `var retentionDays = tenant.AuditLogRetentionDays <= 0 ? 90 : …` then `_db.AuditLogs.RemoveRange(expired)` — a **hard delete**. `Tenant.cs:319` — `public int AuditLogRetentionDays { get; set; } = 90;`. The comment at `:315` promises *"Enterprise=2555"* but **nothing seeds it**. A daily job destroys the evidence at ~1/28th of the documented retention.

### Auditor pushback on the brief (both accepted)

1. *"No user story backs §6, so no ledger tracks it"* is mostly right, not wholly — §6.1 is tracked as US-PLT-002/ISSUE-277, §6.2 partly as ISSUE-203, §6.9's grace window as BUG-002. §6.2–§6.12 are otherwise genuinely untracked, **and that is exactly where the gaps concentrate** — the brief's core premise holds.
2. It **overruled its own sub-sweep** which reported `Rls:Enabled` defaults to false — that reading was stale (pre-`b4c61945`). Verified: `appsettings.json:20-22` is `true`, with a startup fail-fast at `DependencyInjection.cs:63-66`. Dev overrides to `false` deliberately.

---

## VERDICT TABLE

| Req | Requirement | Verdict | Evidence |
|---|---|---|---|
| **6.1-a** | Zero cross-tenant leakage: EF filter + RLS + integration tests | **PARTIAL** | All 3 layers exist. EF layer has **7 holes** and is fail-open when unresolved. RLS layer complete + guard-tested |
| 6.1-a-i | EF filter on *every* tenant-scoped entity | **PARTIAL** | 132 `HasQueryFilter` vs 135 tenant-scoped entities → GAP #1 |
| 6.1-a-ii | PG RLS policies, ON by default | **IMPLEMENTED** | `appsettings.json:20-22`; fail-fast `DependencyInjection.cs:63-66`; ENABLE+FORCE reconciler `DbInitializer.cs:143-160`; `TenantGucConnectionInterceptor.cs:43-48`. Reflection-driven over `information_schema`, so new tables auto-policy |
| 6.1-a-iii | Isolation integration tests | **IMPLEMENTED** | `RlsIsolationPostgresTests.cs:46-58` (Testcontainers, real `hrm_app` NOBYPASSRLS role), coverage guard `:283`; + 4 more suites. **Genuinely load-bearing — not test theater. The best artefact in this audit.** |
| **6.1-b** | Request without valid tenant context must be **rejected, not assumed** | 🔴 **MISSING** | The six-link chain above → GAP #2 |
| 6.1-c | Isolation tests on every PR; failures block merge | PARTIAL | `.github/workflows/ci-gate.yml:16-22,54` runs full `dotnet test` on PRs into `main` and `test/local-subdomains`. "Blocks merge" depends on branch protection — **not statically verifiable** |
| 6.2-a | API read p95 ≤ 400 ms | UNVERIFIABLE (mechanism ✔, measured ✔) | `perf/scripts/01,03,05`; 243 `HasIndex`; `PagedResult` (74 refs). k6 asserts exactly 400ms; recorded run met SLA. **Never runs in CI** |
| 6.2-b | API write p95 ≤ 800 ms | **PARTIAL — measured MISS** | `perf/scripts/02-auth-login.js:14-27`. Login p95 **3.86 s @ 20 VU vs 800 ms** (ISSUE-203, BCrypt). WorkFactor 12→11, still ~370 ms/verify |
| 6.2-c | Page TTI p95 ≤ 2.5 s on 4G | UNVERIFIABLE — **no measurement path** | Budgets `angular.json:43-54`; 210 lazy routes; 209 `OnPush`. **No Lighthouse CI, no web-vitals, no 4G throttle** |
| 6.2-d | Payroll 5,000 employees ≤ 10 min | UNVERIFIABLE — **no measurement path** | `PayrollRunProcessor.cs:235-239,466,634` — **loads all employees in one `ToListAsync` and commits one `SaveChanges`.** No chunking, no resumability, no timing instrumentation, no perf script |
| 6.2-e | ≥ 10,000 concurrent users | UNVERIFIABLE — no path | Max authored load = **50 VU**, 200× below target |
| 6.2-f | Bulk import 10k rows ≤ 5 min | UNVERIFIABLE — mechanism ✔ | `BulkEmployeeImportService.cs:43` (`BatchSize = 100`), per-batch rollback. Only a 500/600 boundary check; **no 10k timing test** |
| 6.2-g | Tenant resolution ≤ 5 ms cached | UNVERIFIABLE — mechanism ✔ | `TenantResolutionMiddleware.cs:239,299,305`, TTL default 5 min. No stopwatch/metric |
| 6.3-a | Stateless API behind LB | PARTIAL | ✔ DataProtection→DB; ✔ SignalR Redis backplane (**optional**); ✘ **in-process rate limiter** `Program.cs:521-546` — limits multiply by N instances |
| 6.3-b | Read replicas; tenant partitioning (Phase 2) | MISSING (documented deferral) | `LeaveReportService.cs:31` says "DEFERRED". **Not a defect** |
| 6.3-c | Workers scale independently from API | MISSING | `Program.cs:305-318` hosts Hangfire **in the API process**; no worker service in `docker-compose.yml` |
| 6.3-d | Accommodate DB-per-tenant later without rewrite | MISSING | No tenant→connection resolver; the routing interceptor is an RLS privilege split, not a tenant split |
| 6.4-a | ≥ 99.5% uptime **per tenant** | PARTIAL | Uptime computed over 30 days (`PlatformMonitoringService.cs:514-541`) but `:511` states plainly it is a **platform** property — **the same number is returned for every tenant.** **Zero alerting anywhere** — nothing consumes `health_probe` but the admin dashboard |
| 6.4-b | `past_due`/`suspended` banner, doesn't break | PARTIAL | ✔ Suspended: `TenantStatusEnforcementMiddleware.cs:56-90` (451) + FE page; ✘ **PastDue defined at `Tenant.cs:330` and never enforced or surfaced** |
| 6.5-a | OWASP Top 10 controls | PARTIAL | ✔ rate limiting, FluentValidation, authz; ✘ **security headers NOT FOUND** — no CSP/HSTS/X-Frame-Options/X-Content-Type-Options in backend or any nginx conf. CSRF absence is defensible (bearer JWT, no cookie auth) |
| 6.5-b | All traffic HTTPS (TLS 1.2+) | PARTIAL | ✘ no `UseHttpsRedirection`/`UseHsts` in `src/backend`; ✔ TLS only in `local-dev/nginx*.conf` (opt-in dev overlay). **`ops/` contains no TLS/nginx config — no production TLS artefact in this repo** |
| 6.5-c | Secrets in Key Vault / Secrets Manager | **MISSING** | No `AddAzureKeyVault`/`SecretClient`/`SecretsManager`/`VaultSharp` anywhere. **A live AES-256 key is committed** at `appsettings.Development.json:37`. Gitleaks is advisory-only (`--exit-code 0`) so it cannot catch a regression |
| 6.5-d | Encryption at rest (`pgcrypto` for PII) | PARTIAL | ✔ app-level AES-256-GCM, `EncryptedFieldRegistry.cs:46-68` (**9 columns**); ✘ **`pgcrypto` in zero migrations**; ✘ no TDE/disk encryption. Design drift from the doc's named mechanism, defensible |
| 6.5-e | Cross-tenant access needs SysAdmin permission, fully audited | **IMPLEMENTED** | `AdminImpersonationController.cs:37-45,59-66`; `ImpersonationService.cs:159-173,245-259`; `TenantAccessGuardMiddleware.cs:38-53`. **Strongest part of §6.5** |
| 6.6-a | Clean Architecture, SOLID | **IMPLEMENTED** | No inward-pointing violation across all 5 csproj |
| 6.6-b | ≥70% coverage App+Domain; ≥85% payroll/leave/isolation | **MISSING** | Coverlet installed (`HRM.Tests.csproj:12`) and **never invoked**; no `--collect` in CI; no `.runsettings`; Karma coverage reporter **off**. Volume is large (4,275 `[Fact]` + ~4,053 FE `it(`) but **the numeric requirement has no measurement path** |
| 6.6-c | Static analysis on every PR | PARTIAL | semgrep + gitleaks both `continue-on-error` + `\|\| true`; no `TreatWarningsAsErrors`. **Everything runs, nothing blocks.** `package.json:11` `"lint": "ng lint"` with **no lint builder and no ESLint config** — would error if invoked; CI never invokes it |
| 6.7-a | Mobile-responsive to 360px | PARTIAL | Real assertion exists — `e2e/cross-browser.spec.ts:20,36-42,65,71` (360×740 no-overflow). **The e2e suite is not run in CI** |
| 6.7-b | WCAG 2.1 AA | PARTIAL | `@axe-core/playwright` installed with **zero `AxeBuilder` usages**. Markup effort real (613 `aria-label`, 34 `aria-live`). Only evidence is a manual 2026-06-30 pass that **found open a11y bugs** (BUG-096/111/112). No automated AA gate |
| 6.7-c | Consistent design system | PARTIAL | ✔ tokens shared; ✘ `shared/components/` holds **exactly one** component — buttons/tables/cards re-declared per feature with inline `var(--brand-primary)` |
| 6.7-d | Tenant-branded login & shell | **IMPLEMENTED** | BE `TenantContextController.cs:28,42-43,76-86` ↔ FE `tenant.service.ts:271,313-314,371-383`. **FE/BE contract verified field-by-field.** All three legs |
| 6.8-a | Latest 2 versions of Chrome/Edge/Firefox/Safari | PARTIAL | **No `browserslist` anywhere** — target undeclared. Karma Chrome-only; Playwright's 3 engines never run in CI; no Edge project |
| 6.8-b | PostgreSQL 14+ | **IMPLEMENTED** | `postgres:17-alpine` runtime + ~45 Testcontainers fixtures. **Inconsistency:** `ci-gate.yml:74` runs migrations against `postgres:16` |
| 6.8-c | .NET 10, Angular 20 | **IMPLEMENTED** | All 5 csproj `net10.0`; `@angular/core: ^20.0.0`; CI pins `10.0.x` |
| 6.9-a | GDPR access, per tenant **and per user** | PARTIAL | ✔ tenant (`TenantDataExportService.cs:69,79,206,638`); ✘ **per-user DSAR NOT FOUND** (searched DSAR/SubjectAccess/Article 15/my-data/personal-data) |
| 6.9-b | GDPR erasure, per tenant **and per user** | PARTIAL | ✔ tenant; ✘ per-user exists only as `IAuditAnonymizationService.AnonymizeUserAsync`, DI-registered, **with zero callers** — orphaned, and it anonymizes audit trails only |
| **6.9-c** | 7-year audit retention; PII access logged | 🔴 **CONTRADICTED** | See C-1. A shipped job hard-deletes at **90 days** vs a documented **7 years** |
| 6.9-d | Per-tenant deletion within a **configurable** grace window | PARTIAL | Mechanism complete and well-built, but `TenantLifecycleService.cs:31` `DefaultGraceDays = 30` is a **hard-coded const**, not from `IConfiguration`, not plan-governed — cited as BUG-002 in its own comment |
| 6.5/6.9 | Audit immutability (implied) | PARTIAL | Convention only — `AuditLogController.cs:19-21` ("append-only by code convention … REVOKE DEFERRED"). `Rls/roles.sql:43,47` grants `SELECT, INSERT, UPDATE, DELETE` on **all** tables to `hrm_app`, audit included. **The runtime role can rewrite audit history** |
| 6.10-a | Idempotent writes on critical endpoints | PARTIAL | **Exactly 4 opt-in endpoints**, each reading the header by hand, two incompatible mechanisms. Leave approve/reject, payroll approve/finalize, bulk-import have **none** |
| 6.10-b | Optimistic concurrency (xmin) on shared records | PARTIAL | Correct where present — **12 of 149 DbSets (~8%)**. Payroll runs and salary structures are shared and have none |
| **6.11-a** | Structured logs with `tenant_id`, `trace_id`, `user_id` on **every** record | PARTIAL | `TenantResolutionMiddleware.cs:167-171` is the **only** enrichment site in the backend, and pushes tenant only. **`user_id` NOT FOUND. `trace_id` NOT FOUND** (`Serilog.Enrichers.Span` planned, never added). **1 of 3 properties, and only post-resolution** |
| 6.11-b | Per-tenant metrics for SLA reporting | PARTIAL | ✔ SQL-backed p95 histogram + API-call usage; ✘ **no `/metrics`, no Prometheus exporter**. `HrmDomainMetrics.cs:14` **explicitly excludes tenant tagging**, so OTel *cannot* produce per-tenant SLA numbers |
| 6.11-c | Distributed tracing | PARTIAL | `ObservabilityExtensions.cs:182-217` fully wired (AspNetCore+HttpClient+Npgsql+HRM, OTLP) — but `:164-166` returns early when mode is `None` and `appsettings.json:135` `"OtlpEndpoint": ""`. **Fully wired, fully dormant out of the box** |
| 6.12-a | Provision a tenant in ≤ 5 min | IMPLEMENTED (timing unverifiable) | `AdminTenantsController.cs:40-46,76,91` + FE routes |
| 6.12-b | Suspend/terminate in a single screen action | IMPLEMENTED | All four actions on one screen (`tenant-monitoring-detail:23-28,121-135,164-167`) |
| 6.12-c | Impersonate with audit **and tenant-admin notification** | IMPLEMENTED | Real service registered (`DependencyInjection.cs:817`), not the `LogOnly` variant — checked for that trap. Caveat: notification failure is swallowed (`ImpersonationService.cs:199-205`) — session proceeds unnotified |
| 6.12-d | Per-tenant export bundle on demand | IMPLEMENTED | `DataExportController.cs:32,51,64,81,103-114` + job + FE |

---

## GAPS RANKED

**Tenant isolation first, per Critical Rule #1.**

### 🔴 #1 — An unresolved tenant context disengages ALL THREE isolation mechanisms · **CRITICAL** · Size: M

The six-link chain verified above. Three independent design decisions compose into a fail-open path, and the guard that exists for exactly this class of bug (`TenantAccessGuardMiddleware`, from BUG-003) **explicitly skips** the case. There is no `RequireTenant` filter anywhere.

**Reachability:** `api` and `app` are both in the shipped reserved-subdomain list. A deployment serving the API at `api.<basedomain>` — the single most conventional layout — would run **every authenticated request unresolved, with all three layers off.** Today's rig uses per-tenant subdomains, so this is **latent, not live**.

*Confidence:* **100%** on the six code facts (orchestrator re-read every one). **85%** that it is exploitable under some plausible deployment topology. *Settled by:* an HTTP test hitting a tenant-scoped endpoint with a valid tenant JWT on a no-subdomain host, asserting 4xx rather than data.

*Smallest fix:* **invert the default.** A terminal middleware or MVC filter that rejects any request reaching a tenant-scoped controller with `IsResolved == false && !IsSystemContext`. That is literally what §6.1 bullet 2 asks for, and it collapses all three sub-gaps at once.

### #2 — Seven tenant-scoped entities have no EF global query filter · HIGH · Size: S

`AuditLog`, `PlanLimitOverride`, `TenantLifecycleEvent`, `TenantScheduledJob` (own `TenantId`), plus `PayrollReportExport` and `TenantLatencyBucket` (**independently found by Pass E**). `ImpersonationSession` carries `TargetTenantId` — deliberately global, not counted.

*Mitigating:* the RLS layer covers all of them (reflection-driven over `information_schema`; `RlsIsolationPostgresTests.cs:283-314` asserts exact set equality). So with RLS on, layer 2 covers them — but the doc's "multiple layers" promise is down to one, and with `Rls:Enabled=false` (dev, or any misconfigured deployment) they are **unprotected**.

*Smallest fix:* add the 6 missing lines **and mirror the RLS coverage guard on the EF side** — a `[Fact]` over `db.Model.GetEntityTypes().Where(et => et.FindProperty("TenantId") is not null)` asserting `GetQueryFilter() is not null`. **The RLS layer has exactly this guard and has zero holes; the EF layer has none and has seven. That is the whole explanation.**

### #3 — 270 `IgnoreQueryFilters()` calls, zero justified, lint advisory-only · HIGH · Size: M

The project already wrote the right rule — `.semgrep/tenant-isolation.yml:6-22`, severity ERROR, with a `// nosemgrep:` escape hatch requiring a one-line reason. **Zero of the 270 carry it**, and `semgrep.yml:24-25,33` runs `continue-on-error: true` + `|| true`. The rule's own header says *"tighten to blocking once the codebase is clean"* — it never got clean. Many uses are certainly legitimate; **the gap is that nobody can tell which.**

### #4 — Audit retention actively violated by a running purge job · HIGH · compliance · Size: S
See C-1. Not merely an unmet requirement — **a running job deleting the evidence the requirement exists to preserve**, with no archival path.

### #5 — No secret manager; a live AES-256 key is committed · HIGH · Size: M
`appsettings.Development.json:37`, self-labelled "safe to commit". Dev-only by convention — but `EncryptedFieldRegistry` covers `employees.national_id`, so anyone running the dev stack with real data encrypts it under a public key.

### #6 — Audit log is append-only by convention only · MED-HIGH · Size: S
`Rls/roles.sql:43,47` grants `UPDATE, DELETE` on all tables to `hrm_app`. Fix: `REVOKE UPDATE, DELETE ON audit_logs … FROM hrm_app;` — purge/anonymize already run privileged.

### #7 — Observability is one-third done and dormant · MED-HIGH · Size: M
`user_id` and `trace_id` absent from logs; OTel wired but inert; per-tenant metrics have no exporter and `HrmDomainMetrics.cs:14` deliberately excludes tenant tags. Combined with **zero alerting**, an incident is diagnosable only after someone opens a dashboard. *Cheapest third:* `Serilog.Enrichers.Span` + a `user_id` push ≈ 10 lines.

### #8 — No alerting; per-tenant uptime is a platform number wearing a tenant label · MED-HIGH · Size: M
`PlatformMonitoringService.cs:511` says so outright. **Either scope the probe per tenant or amend §6.4 — a requirement that is unmeasurable is worse than one that is unmet.**

### #9–#18 (condensed)
9. **Coverage requirement has no measurement path** — coverlet never invoked, Karma coverage off. A ≥70%/≥85% target that is never computed can be neither met nor missed. *S*
10. **The `lint` script is broken and CI never runs it** — `ng lint` with no builder and no ESLint config. *S*
11. **`TenantStatus.PastDue` defined, never enforced or surfaced** — indistinguishable from `active`. *S*
12. **Idempotency covers 4 endpoints, not "critical endpoints"** — extract one `[Idempotent]` filter; storage and index already exist. *M*
13. **Per-user GDPR access and erasure do not exist** — tenant scope well-built; user scope absent/orphaned. *L*
14. **No security headers; no production TLS artefact in the repo** — *70% confidence* this is a genuine gap vs out-of-repo infra. *S*
15. **Workers co-hosted with the API; rate limiter per-process** — every published limit multiplies by instance count, silently weakening the `auth-login` brute-force control. *M*
16. **No measurement path for 4 of 7 perf targets; one measurably missed** (login p95 3.86 s vs 800 ms). Also: `PayrollRunProcessor` materializes an entire run in memory and commits one `SaveChanges` — the shape that fails the 10-minute target with no resumability. *M*
17. **Browser support undeclared, effectively Chrome-only in CI**; `ci-gate.yml:74` runs migrations on `postgres:16` while runtime is 17. *S*
18. **a11y and responsive assertions exist but never execute** — axe installed with zero usages; a real 360px overflow assertion no workflow runs. **Highest ratio of coverage-gained to work-done in the list: add a Playwright job.** *S*

---

## COVERAGE SUMMARY

```
Requirements audited: 44 | IMPLEMENTED: 11 | PARTIAL: 22 | MISSING: 5 | UNVERIFIABLE: 5 | CONTRADICTED: 1
```

**The single most useful finding in this pass is a pattern, not an item.** The failures concentrate overwhelmingly at **leg 2 (reachable/wired)**, not leg 1. This codebase builds the mechanism and then ships it switched off, unmeasured, or advisory:

| Built | Shipped as |
|---|---|
| OpenTelemetry tracing | wired, **dormant** (blank endpoint) |
| `@axe-core/playwright` | installed, **zero usages** |
| coverlet | present, **never invoked** |
| semgrep tenant-isolation rule | running, **non-blocking** |
| Playwright 3-engine e2e | configured, **not in CI** |
| `AnonymizeUserAsync` | DI-registered, **zero callers** |
| `TenantStatus.PastDue` | defined, **never enforced** |

**Seven distinct instances of one failure mode.** This is not an engineering-capability gap — it is a **last-mile activation gap**, and most items on it are S-sized. That makes it the cheapest large win available.

**By layer:** §6.1 (isolation) and §6.12 (operability) are strongest — the RLS work is genuinely high quality. §6.11 (observability), §6.6 (maintainability gates) and §6.9 (compliance) are weakest.

**The brief's hypothesis is confirmed:** the two hardest findings (#1 fail-open, C-1 audit retention) are both in §6, both untracked by any ledger, and both would have stayed invisible to `/test-all` and `/implement-all` **because no story and no TC names them.**

**Asymmetry worth naming:** the EF filter layer has 7 holes; the RLS layer has zero. The difference is that RLS has a coverage-guard test and EF does not. **Hand-maintained lists drift; reflection-driven ones don't.** One test closes the class permanently.

---

## CONFIDENCE

**Overall: 85%.**

| Item | Confidence | What would settle it |
|---|---|---|
| Fail-open chain (code facts) | **100%** | Orchestrator re-read all six links |
| Fail-open chain (exploitability) | **85%** | Live HTTP probe against a reserved/absent subdomain with a valid tenant JWT |
| 7 EF filter holes | **95%** | Mechanical set-difference; re-derivable in one command |
| RLS on by default | **95%** | Read directly; contradicts the file's own stale comments, so re-verified twice |
| C-1 audit retention | **90%** | Code path unambiguous. Residual: a deployment could set 2555 per tenant — nothing in the repo does |
| "Failures block merge" | **50%** | Requires GitHub branch-protection settings, invisible from the tree |
| Production TLS | **70%** | May terminate at infra outside this repo — marked PARTIAL for that reason |
| §6.10-b "shared records" | **70%** | The doc does not define the term; read strictly against payroll/salary |

**What limited this pass:** no running stack, so every §6.2/§6.4 number and the #1 reachability question stay static-only; GitHub branch protection is invisible from the tree; **no test was executed** — existence only, so *"the isolation tests pass"* is explicitly **not** a claim made here.

**Auditor process disclosure (recorded as given):** it shelled one `sort -u` into `/tmp` while computing the filter/entity set-difference before switching to process substitution — one scratch file outside the repo, no repo/`docs/`/`src/` file written. Its contract says "never write files, full stop"; it reported the slip rather than omitting it. Correct behaviour.

---

## OUT-OF-LANE

- **type:** risk · **severity:** 🔴 **CRITICAL** · **where:** `TenantResolutionMiddleware.cs:88-93` · `TenantAccessGuardMiddleware.cs:36-40` · `ConnectionRoutingInterceptor.cs:92-93` · **what:** an authenticated request with an unresolved tenant context disables the EF query filter (tautological predicate) **and** routes to the BYPASSRLS `hrm_owner` role — both isolation layers off at once; the BUG-003 guard explicitly skips this case. Reachable if the API is ever served from a reserved subdomain (`api`, `app`, `www`) or the apex domain. · **suggested-action:** **file as a Critical finding under Critical Rule #1.** Add a fail-closed `RequireResolvedTenant` filter and an HTTP-level xUnit test asserting 4xx for a tenant-scoped endpoint on a no-subdomain host with a valid tenant JWT.
- **type:** test-integrity · **severity:** HIGH · **where:** `AppDbContext.cs:268-796` vs `RlsIsolationPostgresTests.cs:283-314` · **what:** the RLS layer has a reflection-driven coverage guard asserting exact set equality; the EF query-filter layer has no equivalent and has consequently drifted to 7 unfiltered entities. · **suggested-action:** mirror the guard as an EF-side `[Fact]`. Small, and it permanently closes the drift class.
- **type:** risk · **severity:** HIGH · **where:** `src/backend` (270 sites) · `.semgrep/tenant-isolation.yml:6-22` · `semgrep.yml:24-25,33` · **what:** 270 `IgnoreQueryFilters()` calls, none carrying the project's own `// nosemgrep:` justification marker, with the detecting rule advisory-only. · **suggested-action:** one triage pass to annotate legitimate uses, then flip semgrep to blocking as its own header recommends.
- **type:** bug · **severity:** HIGH · **where:** `AuditLogPurgeService.cs:41-52` · `Tenant.cs:315,319` · **what:** a daily job hard-deletes audit logs at 90 days against a documented 7-year requirement; the "Enterprise = 2555" promise exists only as a code comment. · **suggested-action:** file as a high-severity compliance finding; raise the default and/or archive before delete.
- **type:** doc-drift · **severity:** MED · **where:** `appsettings.json:3` · `DbInitializer.cs:66` · `observability-otel-grafana-plan.md:14-16` · **what:** three documents describe a pre-flip state the code has moved past. **Actively misleading — it produced a wrong answer in one of this audit's own sub-sweeps.** · **suggested-action:** refresh the three status blocks.
- **type:** risk · **severity:** MED · **where:** `appsettings.Development.json:37` · **what:** a live base64 AES-256 field-encryption key is committed while gitleaks runs advisory-only and cannot block a future real one. · **suggested-action:** move the dev key to user-secrets/`.env`; make gitleaks blocking.
