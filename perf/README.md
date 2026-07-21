# Performance (k6) harness — Track B

Companion to [../docs/QA/plans/INTEGRATION-PERF-TEST-PLAN.md](../docs/QA/plans/INTEGRATION-PERF-TEST-PLAN.md).
Scripts + seed are **committed**; run results (`results/`, CSV) are **gitignored**.

## Prerequisites
- Backend on `http://localhost:5000` (run with `ASPNETCORE_ENVIRONMENT=Development` so user-secrets load).
- PostgreSQL 18 `hris_dev_db` reachable; `k6` on PATH.
- Dedicated **`perf`** tenant seeded (below). Never seed acme/techoneglobal.

## 1. Seed (B1)
Direct SQL, bypasses the broken employee-no generator (BUG-093) with explicit `employee_no`.
Replicates the 8 built-in roles + permissions and creates `perfadmin@perf.test`
(password `Admin@123!`).

> **✓ Self-contained on any DB (DF-53).** The seed sources the 8 built-in roles from `acme` if present,
> else `e2e`, else any tenant that already has them seeded — and copies perfadmin's password hash from
> `tenantadmin@acme.test` when acme exists, otherwise derives a fresh bcrypt of the same `Admin@123!` via
> `crypt()` (auto-`CREATE EXTENSION IF NOT EXISTS pgcrypto`). So it works unchanged on a normal dev DB and
> on a freshly-migrated throwaway DB (`DbInitializer` seeds only `platform` + `e2e`) — no more
> `fk_user_tenants_users_user_id` error or permission-less admin. Validated 2026-07-21 end-to-end on a
> fresh `postgres:17-alpine` (migrations → e2e-only, no-acme → seed → perfadmin has Tenant Admin + a
> `crypt`-verified `Admin@123!` hash) AND on an acme-present DB (hash copied verbatim from acme).

```bash
# from src/backend/HRM.Api, with PGPASSWORD set from user-secrets (never echo it):
PSQL="/c/Program Files/PostgreSQL/18/bin/psql.exe"
"$PSQL" -h localhost -U developer -d hris_dev_db \
  -v perf_tid="'11111111-2222-3333-4444-555555555555'" -v emp_count=5000 \
  -f perf/seed/seed-perf-tenant.sql
```
The seed is idempotent (resets perf rows first). Use `emp_count=1000` for the smaller arm.

## 2. Run scenarios (B3)
All scripts take env vars `BASE_URL`, `SUBDOMAIN`, `EMAIL`, `PASSWORD` (defaults target perf@localhost:5000).

| Script | Scenario | Load | Key SLA |
|---|---|---|---|
| `scripts/smoke.js` | sanity | 1 VU, 1 iter | all 200 |
| `scripts/01-hot-reads.js` | employee list / widgets / context / reports | 50 VU / 5 min | list p95 < 400ms, err < 1% |
| `scripts/02-auth-login.js` | login throughput (BCrypt) | ramp → 20 VU / 2 min | login p95 < 800ms |
| `scripts/03-scale-reads.js` | large-page list + report aggregation + export @ 5k | 30 VU / 3 min | reads < 400ms, reports < 800ms, export < 2000ms |
| `scripts/04-bulk-import-boundary.sh` | sync→async boundary (US-CHR-010 FR-7, >500 rows) | 500 + 600 row probe | 500=sync, 600=async(jobId) |

```bash
k6 run --summary-export perf/results/01-hot-reads.summary.json perf/scripts/01-hot-reads.js
```

## 3. Teardown (B5)
```bash
"$PSQL" -h localhost -U developer -d hris_dev_db \
  -v perf_tid="'11111111-2222-3333-4444-555555555555'" \
  -f perf/seed/teardown-perf-tenant.sql
```
Deletes ONLY perf-tenant rows + `perfadmin@perf.test`, by exact id. The trailing residue
query must return all zeros. acme/techoneglobal are never touched.

## Notes
- Tenant resolution in dev is via the `X-Tenant-Subdomain: perf` header (no hosts-file entry needed).
- `directory` endpoint returns 403 for Tenant Admin (different perm) — excluded from the read mix.
- Bulk-import routing is **count-based before validation**, so the boundary probe is valid even if rows fail validation.
