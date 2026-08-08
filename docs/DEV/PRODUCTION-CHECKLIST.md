# Production readiness checklist

**Status: 2026-08-06.** Every code blocker is closed (PRs #459–#476). What remains below is
**infrastructure and one business decision** — actions that need credentials, a maintenance window, or a
call about customers' money. None of it can be done from the repo.

Work top to bottom. §1 and §2 are hard gates: getting them wrong takes the platform down or silently
disables tenant isolation. §3–§5 can follow the first deploy.

> **How to read the check queries.** Where a verification step gives SQL, the *result* is the gate — not
> "the command ran". Several of this project's worst near-misses were green checkboxes nobody had earned
> (see [Lessons](#7-lessons-that-earned-their-place) at the bottom).

---

## 0. Before you start

- [x] **`ci-gate` has run green on `test/local-subdomains`** — run 31189082810, 2026-08-07: 5297/0, all three jobs green. It took ISSUE-361 to get there: once the trigger was fixed the gate ran and failed *every* time, because Hangfire was reading a blank-password connection string. This box is now earned rather than assumed.
      It triggered on PRs into `main` only until 2026-08-05, and `main` is stale — so the gate had not run on
      a merged PR since **2026-07-01**, while the RLS Testcontainers suites landed **2026-07-10/11**. They had
      literally never run in CI. Fixed in #474; this box stays unticked until a run actually completes.
- [ ] **A production-shaped restore exists** and §1 has been rehearsed on it end to end, *including the
      rollback*.
- [ ] **Backups verified restorable** — `ops/backup/restore.sh`. Note that after §1 the dump/restore scripts
      need `hrm_owner` credentials, or they come back empty per table (RLS applies to `pg_dump` too).

---

## 1. Tenant isolation — the RLS flip

The largest security gain still outstanding, and the highest-risk step. Full detail and rollback:
[`FLIP-READINESS-2026-08-05.md`](../../src/backend/HRM.Infrastructure/Persistence/Rls/FLIP-READINESS-2026-08-05.md).

### 1.1 Provision the roles

`roles.sql` is an **ops bootstrap script, run once per database by a DBA**. The app never owns role DDL.
Passwords are passed as psql variables so no secret enters source control. It is idempotent.

```bash
export HRM_APP_PASSWORD='<strong password>'
export HRM_OWNER_PASSWORD='<different strong password>'

psql -h <prod-host> -U postgres -d <prod-db> \
     -v hrm_app_password="$HRM_APP_PASSWORD" \
     -v hrm_owner_password="$HRM_OWNER_PASSWORD" \
     -f src/backend/HRM.Infrastructure/Persistence/Rls/roles.sql
```

- [ ] Script applied.
- [ ] **Transfer ownership of existing tables** — `roles.sql` creates the roles but does not reassign what is
      already there:
      ```sql
      REASSIGN OWNED BY <current_migration_role> TO hrm_owner;
      ```

### 1.2 Verify the roles — all four must hold

```sql
-- (a) hrm_app must NOT bypass RLS; hrm_owner must. Neither may be superuser.
SELECT rolname, rolcanlogin, rolbypassrls, rolsuper
FROM pg_roles WHERE rolname IN ('hrm_app', 'hrm_owner');

-- (b) every public table owned by hrm_owner — expect 0
SELECT count(*) AS not_owned FROM pg_tables
WHERE schemaname = 'public' AND tableowner <> 'hrm_owner';

-- (c) hrm_app can write everything the app writes
SELECT table_name, string_agg(privilege_type, ',')
FROM information_schema.role_table_grants
WHERE grantee = 'hrm_app' AND table_schema = 'public' GROUP BY 1;

-- (d) THE check: zero tenant-scoped tables missing their policy — expect 0 ROWS
SELECT c.table_name
FROM information_schema.columns c
JOIN information_schema.tables t
  ON t.table_name = c.table_name AND t.table_schema = 'public' AND t.table_type = 'BASE TABLE'
WHERE c.table_schema = 'public'
  AND c.column_name = 'tenant_id'
  AND c.table_name NOT IN ('users', 'tenants')   -- deliberate global carve-outs
  AND NOT EXISTS (SELECT 1 FROM pg_policies p
                  WHERE p.schemaname = 'public' AND p.tablename = c.table_name
                    AND p.policyname = 'tenant_isolation');
```

- [ ] (a) roles have the right flags · [ ] (b) `not_owned = 0` · [ ] (c) grants cover every written table ·
      [ ] (d) **zero rows**

> **Do not check a table count against a memorised number.** It was 120 at the 2026-07-11 validation and 135
> on the current dev database — both correct for their schema. Query (d) compares the estate against itself,
> which is why it replaced the old "abort unless it says 137" instruction (#473).

### 1.3 Hangfire needs the privileged role

Hangfire bootstraps its own schema (DDL) and its tables carry no tenant policy. `hrm_app` has no rights there.

- [ ] Hangfire storage points at **`PrivilegedConnection`**.
- [ ] `hangfire` schema owned by `hrm_owner` (else `GRANT CREATE ON DATABASE`).
- [ ] Confirmed no `hangfire.*` table carries a `tenant_id` column.

### 1.4 Deploy in two steps — do not skip the first

**`Rls:Enabled` now defaults to `true` in committed `appsettings.json`** (#476), so step 1 needs an explicit
override.

- [ ] **Step 1 — connections repointed, enforcement still OFF.**
      `DefaultConnection`→`hrm_app`, `PrivilegedConnection`→`hrm_owner`, Hangfire→`hrm_owner`, and
      `Rls__Enabled=false` as an environment override. Confirm the app is healthy.
      *This separates a connection-routing failure from an RLS failure. Debugging both at once is where these
      go wrong.*
- [ ] **Step 2 — remove the override so `Rls:Enabled` returns to its default `true`.** Restart.

> The app **refuses to start** if `Rls:Enabled=true` while `PrivilegedConnection` is blank. That is
> deliberate: with routing inert, migrations, the RLS reconciler's own `ALTER TABLE … FORCE` and Hangfire's
> bootstrap would all run as the unprivileged role and half-apply. A silent downgrade would leave you
> believing isolation is on while it is off — worse than an outage, because nobody investigates a clean start.

### 1.5 Smoke test — in this order

The middle four are the flows that were broken under RLS and fixed in #472. They are now covered by the
RLS-ON HTTP harness, but this is the production confirmation.

- [ ] Login
- [ ] Dashboard read
- [ ] **Tenant switch**
- [ ] **Workspace switcher lists ALL tenants** (not just the current one)
- [ ] A write
- [ ] **Password reset — then confirm sessions in the user's OTHER workspace are dead**
- [ ] A system endpoint on the **platform** host
- [ ] One Hangfire job cycle
- [ ] Cross-tenant negative test: tenant A cannot see tenant B

Then:
```sql
SELECT relname, relrowsecurity, relforcerowsecurity FROM pg_class
WHERE relnamespace = 'public'::regnamespace AND relrowsecurity;
```

### 1.6 Rollback — roughly one restart

- [ ] Set `Rls__Enabled=false`, restart, confirm the `DISABLED … N table(s)` log line and that the `pg_class`
      flags cleared. Policies remain (inert without ENABLE).

> **Not everything reverts with the flag.** `roles.sql` grants, table ownership and the Hangfire
> `GRANT CREATE` are one-way ops changes. Benign to leave, but they are not "flip a boolean" reversible.

---

## 2. Secrets and keys that must be set

The app will not start without these — by design (blank in committed config).

- [ ] `ConnectionStrings:DefaultConnection` — `hrm_app`
- [ ] `ConnectionStrings:PrivilegedConnection` — `hrm_owner`
- [ ] `Jwt:PrivateKey`
- [ ] `Encryption:ActiveKeyId` + `Encryption:Keys:<id>` — the AES-GCM field-encryption ring (PII columns)

### 2.1 Back up the Data Protection ring — and never prune it

**File encryption at rest (ISSUE-359, #469–#471) derives its keys from the Postgres-persisted ASP.NET Data
Protection ring.** Every stored file — payslips, employee documents, offer letters, report exports,
bulk-import spreadsheets — is sealed with a key derived from it.

- [ ] The `data_protection_keys` table is included in backups.
- [ ] **A database restore and a file restore must stay in step.** Files restored against a ring that no
      longer contains their key are unrecoverable.
- [ ] The ring is **never pruned**. Files outlive the default 90-day key rotation by years.

---

## 3. Malware scanning — ClamAV

Committed default is a **blank host**, which selects the allow-with-log stub so local dev, the xUnit gate and
CI stay green without a daemon. Production must set a real one.

- [ ] A `clamd` is reachable (daemon on the app host, or the `clamav/clamav` container — ~1.5–2 GB RAM).
- [ ] `VirusScanning__ClamAv__Host=<clamd-host>` set **before app start** (optionally `__Port=3310`).
- [ ] `FailOpen` left **false** — fail-closed. An unreachable daemon returns `Infected("scan-unavailable")`
      and the upload is rejected with a 400; nothing is ever stored unscanned.
- [ ] Verified: stop `clamd`, attempt an upload, confirm it is **rejected** rather than accepted.

> That last check matters more than it looks. The fail-closed path had **no test at all** until #472 — the
> existing arms covered EICAR detection and clean content, both of which require the daemon to be *up*. An
> always-fail-open regression would have left every virus-scanning test green while production silently
> stopped scanning.

---

## 4. Error tracking — GlitchTip

Self-hosted; see [`ops/glitchtip/README.md`](../../ops/glitchtip/README.md).

- [ ] **`ENABLE_OPEN_USER_REGISTRATION=false`** — the sharp edge. On a reachable instance, anyone who finds
      the URL can register until this is set.
- [ ] DSN configured per component; tenant tags flowing.
- [ ] Instance not publicly reachable, or behind auth.

---

## 5. Authentication and capacity

- [ ] **Confirm the production Entra `RedirectUri` is NOT on a tenant-shaped subdomain.**
      Three `SsoSignInAsync` code paths are safe *because* the redirect resolves with no tenant context
      (routing them to the privileged connection). If the production URI sits on a tenant subdomain, those
      three break for every tenant except that one. *Confidence this is currently fine: 80% — verified
      against dev config only.*
- [ ] **Re-measure login throughput if production has fewer than 8 cores.**
      BCrypt work factor is now **11** (`Authentication:PasswordHashing:WorkFactor`), measured at ~370 ms per
      verify and ~21 logins/sec on 8 cores — which clears the 800 ms p95 SLA at 20 concurrent users, but with
      thin margin. Run the k6 login scenario before trusting it. The floor is 10 (OWASP); the app refuses to
      start below it.
- [ ] `RehashOnLogin` left **true** so existing users migrate off the old cost factor on next sign-in.
      Without it a factor change only affects new passwords and the SLA never actually moves.

---

## 6. BUG-291 — ✅ CLOSED 2026-08-07, no action required

**Was** the one open business decision. It closed on two independent grounds:

1. **The accrual code is fixed** (2026-07-30) — every accrual written since is correct.
2. **No affected data exists.** The residual only ever concerned balances written BEFORE that fix, and there is
   no production or staging environment holding real tenant leave data. Exposure is zero by construction.

**Verified live on 2026-08-07** (against seeded data, since no real data exists): detection is correct
(`credited 12 / should-have 3 / over 9` at 31 Mar), the over-credit **converges within the leave year**
— measured **9 → 6 → 3 → 0** across the quarters — and the correction endpoint is dry-run by default,
idempotent, and writes an auditable `Adjusted` entry rather than editing history.

**The one thing to remember for later:** if you ever **import historical leave data** (migrating a customer
off another HRIS), an import that writes `Accrual` ledger rows with a NULL `AccrualPeriod` recreates exactly
this shape. Run the exposure report afterwards — the correction tool is ready and proven.

- [ ] *(only if historical leave data is imported)* run
      `GET /api/v1/tenant/leave-entitlements/accrual-over-credit-exposure?asOfDate=…` and, if non-zero, decide
      correct / honour / block.

> ⚠ The report can serve a **stale** result if leave types were inserted by direct SQL — the second-level cache
> is invalidated by API writes, not raw DML. Query the database directly, or make one API write first, before
> trusting a zero. This nearly produced a false defect report against the tool during verification.

---

## 7. Lessons that earned their place

Kept because each one cost real time and would otherwise be repeated.

| Lesson | What it cost |
|---|---|
| **"The workflow exists" ≠ "the workflow ran."** | CI had not run on a merged PR for five weeks; the RLS suites cited as pre-flip evidence had never run once. |
| **A test can be green because it's wired to nothing.** | A decorator that could be un-wired from DI, and a `using` that could be deleted, both with zero test failures. |
| **Hand-maintained lists rot silently.** | A link guard listed 2 emitted links while the code emitted 4 — reporting green while the tenant-owner welcome email was dead. Rebuilt to discover from source; it then found 13 dead dashboard links in no ledger. |
| **Tests can assert the bug.** | Three tests asserted dead URLs, encoding the defect as expected behaviour. |
| **The ledger is not evidence.** | Measured ~62% wrong in the OPEN direction: six findings marked open were already fixed. Grep the code first. |
| **A count you memorise becomes a false gate.** | "Abort unless the reconciler says 137" would have fired on a perfectly good flip; the number changes with every new table. |
