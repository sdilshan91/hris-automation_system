---
id: TC-PLT-002-RLS
user_story: US-PLT-002
module: Platform
priority: critical
type: security
status: automated
created: 2026-07-24
automated: 2026-07-24
defect:
  - ISSUE-277
---

# TC-PLT-002-RLS: PostgreSQL Row-Level Security enforces tenant isolation as a second layer beneath the EF query filter (real Postgres)

## 1. Test Objective
Verify that PostgreSQL Row-Level Security (RLS) — the `tenant_isolation` `USING` + `WITH CHECK` policies
keyed off the `app.current_tenant` GUC — isolates tenants at the **database engine** even when the
application layer is deliberately bypassed (raw SQL, a misused `IgnoreQueryFilters()`, an untenanted
job). RLS is proven against the **non-`BYPASSRLS` `hrm_app`** login role (the runtime path); migrations/
seeding run on the `BYPASSRLS` `hrm_owner` role. The suite also proves the production request-path GUC
mechanism (`TenantGucConnectionInterceptor`, ISSUE-277 — a tx-less `set_config` at connection open that
replaced the retired `TenantTransactionBehavior`), the flag-gated reconciler
(`DbInitializer.ReconcileRowLevelSecurityAsync`, reversible by `Rls:Enabled`), and the privileged-vs-app
connection routing (`ConnectionRoutingInterceptor`).

> **Implementation deviation from the story (flag):** US-PLT-002 FR-3 mandates `SET LOCAL` inside an
> ambient per-request **transaction** (`TenantTransactionBehavior`). That mechanism was **retired** because
> the per-request transaction threw under `EnableRetryOnFailure` and nested with own-transaction handlers.
> The shipped mechanism is a **session-scope `set_config('app.current_tenant', …, is_local=>false)` on every
> connection open** (`TenantGucConnectionInterceptor`), re-set per open so it cannot leak across pooled
> connections. The tests below assert the *shipped* mechanism, not the story's original FR-3 wording.

## 2. Related Requirements
- User Story: US-PLT-002
- Acceptance Criteria: AC-1 (raw SQL + `IgnoreQueryFilters` still isolated), AC-2 (GUC set per request/
  connection, pooling-safe), AC-3 (`WITH CHECK` rejects cross-tenant writes), AC-4 (privileged bypass path
  keeps working), AC-6 (real-Postgres suite proves bypass-resistant isolation)
- Functional Requirements: FR-1 (RLS on every `tenant_id` table except `users`), FR-2 (`USING`+`WITH CHECK`),
  FR-4 (non-`BYPASSRLS` app role + separate privileged role), FR-5 (EF filters retained)
- NFR-2 (no session-variable bleed across pooled connections), NFR-3 (reconciler reversible)
- Finding: ISSUE-277 (per-request transaction incompatible with retry strategy → switched to the connection interceptor)

## 3. Preconditions
- A real Postgres 17 container (Testcontainers). The EF InMemory provider implements no RLS, so this arm is
  executed by the orchestrator's Postgres run, not the agent's Docker-less verify gate.
- Two production login roles provisioned as the superuser (mirroring `roles.sql`): `hrm_app` (LOGIN,
  `NOBYPASSRLS`) and `hrm_owner` (LOGIN, `BYPASSRLS`).
- Migrations applied (schema + the dormant `tenant_isolation` policies from
  `20260710120000_Platform_RlsPolicies_Dormant`); enforcement ENABLE+FORCE'd (by the reconciler or a
  simulated reconcile) on every `tenant_id` table except `users`/`tenants`.
- Two seeded tenants with **deliberately different row counts** (A=2, B=3 employees) so any cross-tenant
  bleed changes the number; plus a NULL-tenant (system) role row.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A employees | 2 | isolation discriminator |
| Tenant B employees | 3 | isolation discriminator |
| System role | `tenant_id = NULL` | admitted to both tenants by `USING`, mintable by neither |
| App role | `hrm_app` NOBYPASSRLS | RLS enforced against it (runtime path) |
| Privileged role | `hrm_owner` BYPASSRLS | migrations/seeding/system + cross-tenant jobs |
| GUC | `app.current_tenant` | unset ⇒ SQL NULL ⇒ fail-closed 0 rows |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Raw SQL + EF on `hrm_app` with the GUC set to A, then B. | Each returns only that tenant's rows (2 for A, 3 for B); the nullable `roles` table shows own role + system role, never the other tenant's role. | `RlsIsolationPostgresTests.GucSetToTenant_RawSqlAndEf_ReturnOnlyThatTenantsRows` |
| 2 | Query on `hrm_app` with the GUC **unset**. | 0 rows (fail-closed): `tenant_id = NULL` is never true — the inverse of the EF filter's unresolved=see-all. | `RlsIsolationPostgresTests.GucUnset_OnAppRole_ReturnsZeroRows_FailClosed` |
| 3 | EF query with `IgnoreQueryFilters()` + an unresolved tenant context, GUC=A. | Returns only A's 2 rows — RLS isolates even when the EF filter is bypassed (the headline backstop). | `RlsIsolationPostgresTests.IgnoreQueryFilters_OnAppRole_IsStillTenantIsolatedByRls` |
| 4 | Insert a `departments`/`roles` row stamped for a foreign tenant (or a NULL system role) while GUC=A. | Rejected with `PostgresException` SQLSTATE 42501 (`WITH CHECK` violation) — no cross-tenant/system writes from an app session. | `RlsIsolationPostgresTests.WithCheck_RejectsForeignTenantInsert_OnStrictTable` · `…WithCheck_RejectsNullTenantRoleInsert_FromAppSession` |
| 5 | Query on `hrm_owner` (BYPASSRLS) with no GUC. | Spans both tenants (2+3=5) — migration/seed/system/cross-tenant paths keep working under enforcement. | `RlsIsolationPostgresTests.PrivilegedOwnerRole_NoGuc_SpansAllTenants` |
| 6 | On ONE pooled connection: tx1 GUC=A → sees A, tx2 GUC=B → sees B, then a no-tx query. | Each tx sees only its tenant; the no-tx query sees 0 — the GUC never leaks into a later reuse of the same physical connection (NFR-2). | `RlsIsolationPostgresTests.IsLocalGuc_DoesNotBleedAcrossPooledConnectionReuse` |
| 7 | Enumerate every mapped entity carrying `TenantId` vs the `pg_policies` `tenant_isolation` set. | The two sets match exactly — every tenant-scoped table is policied, `users` is NOT, no stray policies (coverage guard for future entities). | `RlsIsolationPostgresTests.EveryTenantScopedEntity_HasRlsPolicy_CoverageGuard` |
| 8 | `TenantJobRunner.RunForTenantAsync(A)` then `(B)` on `hrm_app`, `Rls:Enabled=true`, reading with `IgnoreQueryFilters()`. | The runner sets the GUC per tenant so a background job sees only A's (2) then B's (3) rows — RLS is the sole isolator. | `RlsIsolationPostgresTests.TenantJobRunner_OnAppRole_RlsEnabled_SetsGuc_SeesOnlyItsTenant` |
| 9 | Drive the real reconciler `Rls:Enabled=true` (ENABLE+FORCE), prove a request through `TenantGucConnectionInterceptor` is isolated, then `Rls:Enabled=false` (DISABLE). | `pg_class` flags flip to (true,true); an interceptor-driven request on `hrm_app` sees only A's rows; owner spans both; after disable the flags clear and `hrm_app` with no GUC sees all rows again (reversible rollback, NFR-3). | `RlsReconcilerPostgresTests.Reconciler_EnablesEnforces_RequestIsIsolated_ThenDisables_Reversibly` |
| 10 | Build the DbContext in the PRODUCTION shape — `EnableRetryOnFailure(3)` + `TenantGucConnectionInterceptor` — and run a plain tenant query, a `CreateExecutionStrategy` own-transaction INSERT, and a second-tenant open. | The plain query succeeds under the retry strategy (the pre-ISSUE-277 per-request transaction threw here); the own-tx INSERT lands tenant-scoped (WITH CHECK satisfied); the second tenant sees only its rows (GUC re-set per open, no leak). | `TenantGucInterceptorRlsPostgresTests.Interceptor_UnderRetryStrategy_SetsGuc_SupportsOwnTx_AndIsolatesPerOpen` |
| 11 | Open the DbContext connection through `ConnectionRoutingInterceptor` under a resolved tenant, a system context, an unresolved ambient, and with a blank `PrivilegedConnection`. | Resolved tenant ⇒ DEFAULT (`hrm_app`) connection; system/unresolved ⇒ PRIVILEGED (`hrm_owner`) connection; blank privileged ⇒ always default (non-breaking until the switch-on). | `ConnectionRoutingPostgresTests` (4 facts) |
| 12 | (Non-relational guard) Run `TenantJobRunner` on the EF InMemory provider with `Rls:Enabled` on/off. | The runner always establishes + publishes the tenant/ambient context but NEVER opens a transaction or runs `set_config` on a non-relational provider (regardless of the flag); exceptions propagate; a second call rescopes. | `TenantJobRunnerTests` (5 facts, unit) |

## 6. Postconditions
- Tenant isolation holds at the database engine as a second layer beneath the EF query filter; an app-layer
  bypass (raw SQL / `IgnoreQueryFilters` / untenanted job) can no longer cross tenants on `hrm_app`. The
  flip is reversible by `Rls:Enabled`; the privileged path is unconstrained by design.

## 7. Test Category Tags
- [x] Happy path (GUC-scoped reads return own tenant)
- [x] Negative test (fail-closed on unset GUC; `WITH CHECK` rejects cross-tenant writes)
- [x] Boundary test (nullable-tenant `roles`; pooled-connection reuse; blank privileged connection)
- [x] Security test (bypass-resistant engine-level isolation, WITH CHECK, privileged-role separation)
- [x] Multi-tenant isolation (the central assertion — raw SQL + `IgnoreQueryFilters` still isolated)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (executed on the orchestrator's real-Postgres run; agent verify gate has no Docker), all carrying `[Trait("TC", "TC-PLT-002-RLS")]`:**
  - `HRM.Tests/Integration/RlsIsolationPostgresTests` (9 facts; the 10th — `AutoClockOutJob_…_DF63` — is `[Trait("TC","TC-ATT-162")]`, not this TC)
  - `HRM.Tests/Integration/RlsReconcilerPostgresTests.Reconciler_EnablesEnforces_RequestIsIsolated_ThenDisables_Reversibly`
  - `HRM.Tests/Integration/TenantGucInterceptorRlsPostgresTests.Interceptor_UnderRetryStrategy_SetsGuc_SupportsOwnTx_AndIsolatesPerOpen`
  - `HRM.Tests/Integration/ConnectionRoutingPostgresTests` (4 facts)
  - `HRM.Tests/Unit/TenantJobRunnerTests` (5 facts — the non-relational no-transaction contract)
- **Deviation note:** the shipped GUC mechanism is `TenantGucConnectionInterceptor` (ISSUE-277), not the
  story's FR-3 `SET LOCAL`+ambient-transaction `TenantTransactionBehavior` (retired). Assertions target the
  shipped code.
