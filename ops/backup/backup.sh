#!/usr/bin/env bash
# Platform backup/retention routine — US-PLT-006 AC-7 / ISSUE-330.
#
# Dumps every persistent Postgres database in the HRM platform to timestamped,
# gzipped SQL files and prunes old dumps. Coverage:
#   - app DB (compose project "hris", service `postgres`) — this ALSO contains the
#     Hangfire schema (Hangfire uses the same Postgres), so one dump covers both.
#   - GlitchTip DB (compose project "glitchtip", service `gt-postgres`, volume
#     `gt-pgdata`) — error-tracking history (US-PLT-006 AC-7).
#
# Usage:
#   bash ops/backup/backup.sh                 # dump both, prune > RETENTION_DAYS
#   RETENTION_DAYS=30 bash ops/backup/backup.sh
#   BACKUP_DIR=/mnt/backups bash ops/backup/backup.sh
#   SKIP_GLITCHTIP=1 bash ops/backup/backup.sh   # app DB only
#
# Cron example (daily 02:30):
#   30 2 * * *  cd /path/to/repo && bash ops/backup/backup.sh >> ops/backup/backup.log 2>&1
#
# Restore: see ops/backup/README.md.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${BACKUP_DIR:-$ROOT/ops/backup/dumps}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"
TS="$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUT"

log() { printf '[backup %s] %s\n' "$(date +%H:%M:%S)" "$*"; }

# dump_db <compose-file> <service> <pg_dump-args...> -> writes <OUT>/<service>-<TS>.sql.gz
dump_db() {
  local compose_file="$1" service="$2"; shift 2
  local target="$OUT/${service}-${TS}.sql.gz"
  log "dumping ${service} (${compose_file})…"
  # -T: no TTY (works under cron). pg_dump runs INSIDE the container so no host client needed.
  # "$@" is the full dump command (e.g. `pg_dump -U … -d …` or `sh -c 'pg_dump …'`).
  if docker compose -f "$compose_file" exec -T "$service" "$@" | gzip > "$target"; then
    log "  -> $target ($(du -h "$target" | cut -f1))"
  else
    log "  !! FAILED dumping ${service} — is the stack up? (docker compose -f ${compose_file} ps)"
    rm -f "$target"
    return 1
  fi
}

rc=0

# App DB (+ Hangfire schema). POSTGRES_USER/POSTGRES_DB come from the container env (docker.env).
dump_db "$ROOT/docker-compose.yml" postgres \
  sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB"' || rc=1

# GlitchTip DB (gt-pgdata). Credentials are fixed in ops/glitchtip/docker-compose.yml (glitchtip/glitchtip).
if [ "${SKIP_GLITCHTIP:-0}" != "1" ]; then
  dump_db "$ROOT/ops/glitchtip/docker-compose.yml" gt-postgres \
    pg_dump -U glitchtip -d glitchtip || rc=1
else
  log "SKIP_GLITCHTIP=1 — skipping GlitchTip dump"
fi

# Retention: prune dumps older than RETENTION_DAYS.
log "pruning dumps older than ${RETENTION_DAYS} day(s) in ${OUT}"
find "$OUT" -maxdepth 1 -name '*.sql.gz' -type f -mtime "+${RETENTION_DAYS}" -print -delete || true

if [ "$rc" -eq 0 ]; then log "backup complete"; else log "backup finished WITH ERRORS (rc=$rc)"; fi
exit "$rc"
