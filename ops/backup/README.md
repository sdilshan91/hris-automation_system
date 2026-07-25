# Platform backup / retention routine

Covers **US-PLT-006 AC-7** and **ISSUE-330** — the platform had no committed backup
routine, so the self-hosted GlitchTip `gt-pgdata` volume (and, in fact, the app DB
itself) was unprotected. This routine backs up **every persistent Postgres database**
in the platform.

## What it covers

| Data | Where | Covered by |
|------|-------|------------|
| **App database** (employees, payroll, auth, tenants, audit_logs, …) | compose `hris` → `postgres` (volume `pgdata`) | `postgres-*.sql.gz` |
| **Hangfire jobs** (recurring jobs, refresh-token cleanup, etc.) | **same app DB** (Hangfire uses the app Postgres) | included in `postgres-*.sql.gz` |
| **GlitchTip error history** | compose `glitchtip` → `gt-postgres` (volume **`gt-pgdata`**) | `gt-postgres-*.sql.gz` |

Dumping the GlitchTip **database** captures everything in the `gt-pgdata` volume that
matters (issues, events, projects, DSNs), which is what AC-7 requires — you don't need
a separate volume-level snapshot.

## Run it

```bash
# dump both DBs, prune dumps older than 14 days (default)
bash ops/backup/backup.sh

# tune retention / output dir / skip GlitchTip
RETENTION_DAYS=30 bash ops/backup/backup.sh
BACKUP_DIR=/mnt/backups bash ops/backup/backup.sh
SKIP_GLITCHTIP=1 bash ops/backup/backup.sh
```

Both stacks must be **up** (`docker compose … ps`). `pg_dump` runs *inside* the
container, so no host Postgres client is needed. Dumps land in `ops/backup/dumps/`
(gitignored) as `‹service›-‹YYYYMMDD-HHMMSS›.sql.gz`.

### Schedule (cron)

```cron
30 2 * * *  cd /path/to/hris-automation_system && bash ops/backup/backup.sh >> ops/backup/backup.log 2>&1
```

For real deployments, ship the dumps off-box (S3/object storage) and back up
`ops/glitchtip/.env` + `docker.env` (the DB credentials/secret keys) **separately and
securely** — they are gitignored and are **not** part of these dumps.

## Restore

```bash
bash ops/backup/restore.sh app       ops/backup/dumps/postgres-YYYYMMDD-HHMMSS.sql.gz
bash ops/backup/restore.sh glitchtip ops/backup/dumps/gt-postgres-YYYYMMDD-HHMMSS.sql.gz
```

Restore overwrites the target DB — take a fresh backup first and confirm the prompt.

## Scope / caveats

- **Dev-oriented.** This is a pragmatic `pg_dump`-based routine for the local/Docker
  stack. A production posture would add off-site retention, encryption-at-rest for the
  dumps, PITR/WAL archiving, and periodic **restore drills** (a backup you've never
  restored is a hope, not a backup).
- Redis (`redisdata`, `gt-redis`) is intentionally **not** dumped — it holds cache /
  transient Celery state, not source-of-truth data.
