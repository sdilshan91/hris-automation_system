# RLS production flip — readiness assessment & runbook (2026-08-05)

**Verdict: GO-WITH-CONDITIONS.** Was **NO-GO** at the start of this assessment. The code blockers are now
closed; the remaining conditions are **ops** steps and **one CI run** that only you can complete.

Supersedes the pre-flip checklist in [`README.md`](README.md) §"remaining" for the items listed here.
The 2026-07-11 manual validation ([`FLIP-VALIDATION-2026-07-11.md`](FLIP-VALIDATION-2026-07-11.md)) remains
useful but is **not sufficient evidence** — see §3.

---

## 1. What was actually wrong

The machinery was never the problem. It is flag-gated, idempotent, two-role routed, config-only reversible,
and `FORCE ROW LEVEL SECURITY` is applied (so the owner-bypass trap does not apply). Policy coverage is
**137/137** tenant-scoped tables with **zero gaps**, and that is enforced rather than lucky:
`RlsIsolationPostgresTests` asserts exact set-equality between EF entities carrying `TenantId` and
`pg_policies`.

The problem was a **semantic inversion nobody had swept for**:

> The EF global query filter is **fail-open** (`!IsResolved || TenantId == current`).
> Postgres RLS is **fail-closed**.
> Therefore every `IgnoreQueryFilters()` written against the fail-open assumption **reverses meaning** when
> the flag flips — silently, with no error, looking exactly like "no such record".

All 69 such call sites on policy-carrying tables (`UserTenant`, `RefreshToken`, `Role`) were classified:

| Bucket | Count | Meaning |
|---|---|---|
| **A — cross-tenant by design** | 7 | Broke under RLS. **All fixed.** |
| **B — within-tenant, safe** | 58 | The row belongs to the ambient tenant anyway. |
| **C — unresolved ambient** | 4 | Already route to the privileged role. Safe, with one caveat (§4). |

58 sites are safe because of one load-bearing fact: `TenantAccessGuardMiddleware` 403s any request whose JWT
tenant differs from the resolved tenant, so `_currentUser.TenantId` **is** the ambient tenant. Blanket-wrapping
these in a privileged scope would have punched 58 unnecessary holes in tenant isolation.

## 2. What was fixed (this PR)

| # | Fix | Why it mattered |
|---|---|---|
| 1 | `CrossTenantScope` + **7 bucket-A sites** | Tenant switching would have been denied for **every** user (`not_a_member`), then 500'd on the refresh-token insert (strict `WITH CHECK`, 42501). The workspace switcher would have collapsed to one entry **and cached that truncated list** in Redis. Password reset / admin force-reset / employee termination would have revoked sessions **only in the current tenant** — a security regression *caused by enabling a security feature*. |
| 2 | `SystemEndpointHostGuardMiddleware` | Nothing bound `/api/v1/system/*` to the admin host. Under RLS a platform-admin request arriving on a tenant subdomain resolves that tenant, routes to the tenant role, and the cross-tenant admin queries return zero rows. Gated on the **resolved context**, not `Host`, so it also works with the dev `X-Tenant-Subdomain` header. |
| 3 | Half-flip fail-fast in `AddInfrastructure` | `Rls:Enabled=true` with a blank `PrivilegedConnection` leaves routing inert, so migrations, the reconciler's own `ALTER TABLE … FORCE`, and Hangfire's schema bootstrap all run as the unprivileged role. Now refuses to start. Refusal is deliberately harsher than degrading to RLS-off: a silent downgrade leaves you believing isolation is ON while it is OFF. |
| 4 | `ci-gate.yml` trigger | Triggered on PRs into `main` only, while the de-facto trunk is `test/local-subdomains`. The gate had not run on a merged PR since **2026-07-01**; the RLS suites landed **2026-07-10/11**, so they had **never run in CI once**. The `README.md` checkbox citing CI coverage as pre-flip evidence was false. |

**Not fixed, and deliberately so:** the audit's `PlatformMonitoringService` NULL-tenant audit insert and the
`TenantLifecycleService` suspend/revoke paths. Those run in **system context**, which already routes to the
BYPASSRLS role — they were only ever going to fail as a *consequence* of #2. Patching them would have treated
the symptom and left the real hole.

## 3. Evidence — and its limits

**New, on real Postgres with RLS enforced** (`RlsIsolationPostgresTests`, hrm_app NOBYPASSRLS):
the bug reproduced (a cross-tenant read returns **0 rows** without the scope), the fix proven (returns the
right rows with it), **isolation restored on exit**, restored **even when the block throws**, and a
cross-tenant write that the strict `WITH CHECK` would otherwise reject. Every arm is mutation-verified.

> One of these arms was **vacuous when first written**: the throw-path test used an `async` lambda, and
> `AsyncLocal` mutations inside a nested async flow never propagate back to the caller — so it reported
> "restored" under a mutation that deleted the restore entirely. Rewritten inline. Worth remembering when
> testing anything ambient-scoped.

**Still missing — the honest gap:** there is **no RLS-ON test of the HTTP surface**. `ApiTestFactory`
hardcodes `Rls:Enabled=false` and is the only `WebApplicationFactory<Program>`, shared by 28 test classes.
Every controller, middleware and handler is tested exclusively RLS-**off**. The fixes above are proven at the
`DbContext`/middleware level, not through a real request. **This is the largest remaining risk** and the reason
the smoke test in §5 is not optional.

## 4. Open caveat — SSO redirect host

Three `SsoSignInAsync` sites are safe **because** the Entra `RedirectUri` is a single fixed URI with no tenant
subdomain, leaving the ambient unresolved (⇒ privileged). **If the production `RedirectUri` is ever hosted on a
tenant-shaped subdomain, those three flip from safe to broken for every tenant except that one.** Confirm the
production value before flipping. *(Confidence the current config is safe: 80% — read from dev config only.)*

## 5. Flip procedure

**Preconditions (all must hold):**

- [ ] `ci-gate` has run **green on `test/local-subdomains`** at least once — this is what finally puts the five
      RLS Testcontainers suites in CI. *(Not `[x]` until a run completes: the whole lesson of item #4 above is
      that "the workflow exists" is not "the workflow ran".)*
- [ ] `roles.sql` applied; `hrm_app` is **NOBYPASSRLS and not the table owner**; `hrm_owner` is **BYPASSRLS and
      owns the tables** (`REASSIGN OWNED`).
- [ ] `hangfire` schema owned by `hrm_owner`; confirm no `hangfire.*` table carries `tenant_id`.
- [ ] Production Entra `RedirectUri` confirmed **not** on a tenant subdomain (§4).
- [ ] Rollback rehearsed on a production-shaped clone.

**Flip:**

1. Restore a prod clone and rehearse steps 2–7 there first, **including the rollback**.
2. Deploy this code with connection strings repointed (`DefaultConnection`→hrm_app,
   `PrivilegedConnection`→hrm_owner, Hangfire storage→hrm_owner) but **`Rls:Enabled` still `false`**.
   This deliberately separates connection-routing failures from RLS failures — debug one thing at a time.
3. Confirm the app is healthy on the two-role setup with enforcement still off.
4. Verify grants cover every table the app writes:
   ```sql
   SELECT table_name, string_agg(privilege_type, ',')
   FROM information_schema.role_table_grants WHERE grantee = 'hrm_app' GROUP BY 1;
   ```
5. Maintenance window. Set `Rls:Enabled=true` and restart.
6. **Watch the startup log for `RLS reconciler: ENABLED + FORCED … on N table(s)`.**
   Do **not** compare `N` against a memorised number — it changes every time a tenant-scoped table is added
   (it was 120 at the 2026-07-11 validation, 135 in the current dev database, 137 once the two Wave 4
   recruitment tables migrate). Compare it against the schema instead. **The check that matters is that this
   returns ZERO rows** — any tenant-scoped table missing its policy is a table RLS will not protect:

   ```sql
   SELECT c.table_name
   FROM information_schema.columns c
   JOIN information_schema.tables t
     ON t.table_name = c.table_name AND t.table_schema = 'public' AND t.table_type = 'BASE TABLE'
   WHERE c.table_schema = 'public'
     AND c.column_name = 'tenant_id'
     AND c.table_name NOT IN ('users', 'tenants')   -- deliberate global carve-outs
     AND NOT EXISTS (
       SELECT 1 FROM pg_policies p
       WHERE p.schemaname = 'public' AND p.tablename = c.table_name
         AND p.policyname = 'tenant_isolation');
   ```

   Then confirm enabled = forced = policied:

   ```sql
   SELECT
     (SELECT count(*) FROM pg_class WHERE relnamespace='public'::regnamespace AND relrowsecurity)      AS enabled,
     (SELECT count(*) FROM pg_class WHERE relnamespace='public'::regnamespace AND relforcerowsecurity) AS forced,
     (SELECT count(*) FROM pg_policies WHERE schemaname='public' AND policyname='tenant_isolation')    AS policies;
   ```

   All three must be equal. **If they differ, or the first query returns any row, abort and roll back.**
   The `hrm_owner bypasses RLS` warning is expected (ISSUE-279).

   > Verified against the running dev database on 2026-08-05: `135 / 135 / 135`, zero unpoliced tenant
   > tables. The two Wave 4 recruitment tables had not yet migrated into that container.
7. Smoke, in this order — the middle four are the flows that were broken and are the point of this PR:
   login → dashboard read → **tenant switch** → **my-tenants (switcher lists ALL workspaces)** →
   a write → **password reset, then confirm sessions in the user's OTHER workspace are dead** →
   a system endpoint **on `admin.*`** → one Hangfire job cycle → cross-tenant negative test (A cannot see B).
8. Verify:
   ```sql
   SELECT relname, relrowsecurity, relforcerowsecurity FROM pg_class
   WHERE relnamespace = 'public'::regnamespace AND relrowsecurity;
   ```

**Rollback (≈ one restart):** set `Rls:Enabled=false`, restart, confirm the `DISABLED … 137 table(s)` log line
and that the `pg_class` flags cleared. Policies remain created and are inert without ENABLE. Revert connection
strings only if the two-role setup itself is implicated.

> **Not "flip a boolean" reversible:** `roles.sql` grants, table ownership (`REASSIGN OWNED`) and the Hangfire
> `GRANT CREATE ON DATABASE` are one-way ops changes. They are benign and safe to leave, but they do not
> revert with the flag.
