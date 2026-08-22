---
name: reference-rls-increment-3a
description: RLS increment 3a — flag-gated ENABLE/DISABLE reconciler, e2e proof, SendEmailJob RLS restructure
metadata:
  type: reference
---

RLS increment 3a (branch `fix/rls-3a-enable-reconciler`, builds on [[reference-rls-increment-2a]]). `Rls:Enabled` STAYS committed-false everywhere → reconciler is a no-op on every current env.

- **Reconciler:** `DbInitializer.ReconcileRowLevelSecurityAsync(db, config, logger, ct)` — PUBLIC static, called from `RunAsync` AFTER migrate+seed (needs `IConfiguration` from scope). Relational-only. `Rls:Enabled=true` ⇒ DO-block `ENABLE + FORCE` on the 2a set (tenant_id column, base table, excl users/tenants, via information_schema); `false` ⇒ `NO FORCE + DISABLE` same set (R7 reversibility — rollback by config+restart, no down-migration). Warns via `SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname=current_user` if the connected role bypasses RLS. SQL is plain literal strings (not interpolated) to avoid the EF raw-SQL analyzer.
- **`ALTER TABLE ... ENABLE ROW LEVEL SECURITY` needs table OWNERSHIP, not just BYPASSRLS.** So in tests migrate as `hrm_owner` (grant `CREATE, USAGE ON SCHEMA public` first), not the superuser — else `42501: must be owner of table`. Production model: hrm_owner owns the schema.
- **E2e proof:** `RlsReconcilerPostgresTests` (ONE ordered [Fact], not split — shared live-DB state). Drives the REAL reconciler + REAL `TenantTransactionBehavior<CountEmployeesRequest,int>` on `hrm_app`: ENABLE→pg_class flags→raw isolation→request-path GUC isolation→owner spans→DISABLE→pg_class clear→hrm_app sees all (reversible). Reads flags via `pg_class.relrowsecurity/relforcerowsecurity`.
- **SendEmailJob (HIGH pre-flip fix):** restructured read→send(OUTSIDE tx)→persist(own committed unit)→rethrow. Now routes DB via `ITenantJobRunner.RunForTenantAsync(tenantId,…)` (sets GUC on hrm_app). Compute `attempts = row.Attempts + 1` OUTSIDE the retry-safe persist unit (retry-safe tx re-runs the delegate → don't `Attempts++` a tracked entity inside it = double-count). Rethrow via `ExceptionDispatchInfo.Throw(sendError)` after persist. Test scope factory needed `ITenantJobRunner` + `IConfiguration` registered.
- **3b follow-ups (deferred, in README checklist):** CI RLS service-container job; long-running by-id jobs (payslips/exports/payroll) hold one tx for whole job under RLS [MED]; service-body DI-scope audit [LOW].
