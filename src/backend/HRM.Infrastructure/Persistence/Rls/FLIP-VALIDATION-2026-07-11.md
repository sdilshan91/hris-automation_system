# RLS Flip-Gate — Local End-to-End Validation (2026-07-11)

**Method:** stood up a dedicated, disposable Postgres 17 container, ran `roles.sql`, made `hrm_owner` own the
schema, pointed `DefaultConnection`→`hrm_app` + `PrivilegedConnection`→`hrm_owner`, set `Rls:Enabled=true`, booted the
real API (migrate + seed + reconciler `ENABLE + FORCE` on **120** tenant tables), and drove real flows as a **business
tenant** (subdomain `e2e`, so requests route to `hrm_app` and hit the GUC path) — login, tenant CRUD, a race-safe
handler, and the session-activity/notification path. This exercises what the RLS Testcontainers suites do NOT: the
whole app end-to-end with the **production DbContext config**.

## VERDICT: ⛔ NO-GO — do NOT set `Rls:Enabled=true` yet.

Two independent blockers make **every** tenant request fail under RLS-on. One is a small fix; the other is an
architectural incompatibility in how the tenant GUC is set. Both were **invisible to the test suite** because the RLS
Testcontainers tests build their own DbContext **without** `EnableRetryOnFailure` and drive the reconciler / raw
`AppDbContext` rather than the real MediatR request pipeline + production services.

---

## Findings

### 🔴 BLOCKER — ISSUE-277: the per-request GUC transaction breaks all tenant requests (two faces, one root)
`TenantTransactionBehavior` sets `app.current_tenant` by opening a **request-wide transaction** and running the whole
handler inside it. Against the production DbContext this fails two ways:

1. **Retry-strategy conflict.** The DbContext is registered with `EnableRetryOnFailure(3)`
   (`DependencyInjection.cs:61`), so a bare `BeginTransactionAsync` throws
   `InvalidOperationException: The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support
   user-initiated transactions` on the **first query of every request** → **login 500'd**, all reads/writes 500. (A
   local `CreateExecutionStrategy().ExecuteAsync` wrap around the behavior fixes THIS face and login/CRUD then worked —
   but see face 2.)
2. **Nested-transaction conflict.** Even with face 1 fixed, any handler that opens its **own** transaction on the
   request DbContext nests inside the behavior's tx and throws
   `InvalidOperationException: The connection is already in a transaction and cannot participate in another
   transaction`. **Reproduced live** on training enrolment (`TrainingService.RunRaceSafeAsync:521` ← `EnrollAsync:245`)
   → **500**. The same pattern (own `CreateExecutionStrategy` + `BeginTransaction` on the request `_db`) exists in
   **`WorkflowRuntimeService.DecideAsync`, `ApplicantConversionService`, `TrainingService`, `AuthService`,
   `TenantDataDeletionService`** — i.e. leave/attendance/workflow approvals, recruitment convert, training enrol, and
   some auth flows would all 500 under RLS-on.

**Root cause:** wrapping the whole request in one transaction to carry a transaction-local GUC is incompatible with
(a) the retry execution strategy and (b) handlers that manage their own transactions.

**Recommended fix (design decision for the team):**
- **(Preferred) Set the GUC at the connection level, not via a request-wide tx.** A `DbConnectionInterceptor`
  (`ConnectionOpenedAsync`) issues `set_config('app.current_tenant', <tid>, false)` (session scope) when a
  non-system tenant is resolved and `Rls:Enabled`, and **resets it on return to the pool** (Npgsql connection reset /
  set-on-every-open) to avoid pooled-connection leakage. This removes the request-wide tx entirely → no retry conflict,
  no nesting; handlers keep their own tx semantics. This is the standard RLS-with-pooling pattern; the reset is the
  part to get right and test.
- **(Alternative) Keep the request tx but (i) wrap it in `CreateExecutionStrategy().ExecuteAsync` AND (ii) make every
  own-tx handler reuse the ambient transaction** (skip its own `CreateExecutionStrategy`/`BeginTransaction` when
  `_db.Database.CurrentTransaction is not null`). Invasive across the 5 services above and changes their retry
  semantics — higher risk than the interceptor.

**Required test (the gap that hid this):** an integration test that runs the real MediatR request pipeline (incl.
`TenantTransactionBehavior`) through a Postgres DbContext configured **with `EnableRetryOnFailure`**, RLS-on as
`hrm_app`, asserting both a simple query AND an own-tx handler (e.g. training enrol) succeed.

### 🟢 FIXED — ISSUE (roles.sql): password variables never substituted (ops bootstrap was broken)
The documented `psql -f roles.sql -v hrm_app_password=… -v hrm_owner_password=…` **failed**: psql does not interpolate
`:'var'` inside `DO $$…$$` blocks, so `CREATE ROLE … PASSWORD :'…'` errored (`syntax error at or near ":"`) and the
roles were never created — an operator following runbook step 1 would be dead in the water. **Fixed** (this PR): the
`CREATE ROLE` now runs at psql top level via a conditional `\gexec` (idempotent + interpolation-safe). Verified: fixed
script creates both roles with correct BYPASSRLS flags + passwords (both authenticate) and is idempotent on re-run.

### 🟡 NOTE — ISSUE-278: Hangfire schema bootstrap needs `CREATE ON DATABASE` on a greenfield RLS deploy [LOW]
On a **fresh** DB, Hangfire (correctly pointed at `PrivilegedConnection`=`hrm_owner`, `Program.cs:258-261`) fails to
install its own schema: `42501 permission denied for database` → recurring-job registration crashes startup. Cause:
`hrm_owner` owns the `public` schema but lacks database-level `CREATE`. **Not a blocker for a real prod flip** (an
existing DB already has the `hangfire` schema from pre-RLS), but a **greenfield RLS-first** deploy must
`GRANT CREATE ON DATABASE <db> TO hrm_owner` (or pre-provision the `hangfire` schema owned by `hrm_owner`). Runbook
updated.

### 🟡 NOTE — ISSUE-279: reconciler bypass-warning false-fires in the correct config [LOW]
On startup the reconciler logs `RLS is ENABLED but the app connection (current_user=hrm_owner) bypasses RLS … point
DefaultConnection at hrm_app`. This fires because `DbInitializer` runs on the **privileged** connection (`hrm_owner`,
by design) — it does NOT mean `DefaultConnection` is misconfigured. Misleading in the correct setup; the check should
target the request-path role, not the reconciler's own connection.

---

## What DID work under RLS-on (once the retry face was locally patched)
- Boot: migrate (98) + seed + reconciler `ENABLE + FORCE` on 120 tables — all as `hrm_owner`. ✅
- `hrm_app` request path: login (200), leave-type read (200) + **write (201, WITH CHECK accepted)**, departments
  (200) — the GUC is set and reads/writes are correctly tenant-scoped. ✅
- **ISSUE-268 live:** the session-activity `refresh_tokens` update fired on authenticated requests with **no 42501** —
  the fresh-scope-through-`ITenantJobRunner` fix works under real RLS-on. ✅

## Remaining pre-flip checklist (after ISSUE-277 is resolved + tested)
Re-run this same local validation; drive an own-tx handler (training enrol / a leave or workflow approval / recruitment
convert) and confirm 2xx; then the deferred `DataExportGeneration`/`HrReportExport` long-tx items and the CI RLS job.
