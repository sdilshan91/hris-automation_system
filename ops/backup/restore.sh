#!/usr/bin/env bash
# Restore a platform Postgres dump produced by ops/backup/backup.sh.
#
# Usage:
#   bash ops/backup/restore.sh app       ops/backup/dumps/postgres-YYYYMMDD-HHMMSS.sql.gz
#   bash ops/backup/restore.sh glitchtip ops/backup/dumps/gt-postgres-YYYYMMDD-HHMMSS.sql.gz
#
# WARNING: this drops+recreates objects in the target DB (the dump is a plain SQL
# restore). Take a fresh backup first and make sure the target stack is up.
set -euo pipefail

target="${1:-}"; dump="${2:-}"
if [ -z "$target" ] || [ -z "$dump" ] || [ ! -f "$dump" ]; then
  echo "usage: bash ops/backup/restore.sh <app|glitchtip> <dump.sql.gz>" >&2
  exit 2
fi
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

case "$target" in
  app)       compose="$ROOT/docker-compose.yml";              service="postgres";    psql='psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"' ;;
  glitchtip) compose="$ROOT/ops/glitchtip/docker-compose.yml"; service="gt-postgres"; psql='psql -U glitchtip -d glitchtip' ;;
  *) echo "unknown target '$target' (use app|glitchtip)" >&2; exit 2 ;;
esac

read -r -p "Restore '$dump' into $target ($service)? This overwrites current data. [y/N] " ans
[ "$ans" = "y" ] || { echo "aborted"; exit 1; }

echo "restoring $dump -> $target …"
gunzip -c "$dump" | docker compose -f "$compose" exec -T "$service" sh -c "$psql"
echo "restore complete."
