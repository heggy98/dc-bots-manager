#!/usr/bin/env bash
# Daily PostgreSQL backup for the docker-compose PostgreSQL stack.
# Usage (cron as the ubuntu user): 15 3 * * * /opt/dc-bots-manager/deploy/oracle/backup.sh >> /var/log/botmanager-backup.log 2>&1
set -euo pipefail

REPO_DIR="${REPO_DIR:-/opt/dc-bots-manager}"
BACKUP_DIR="${BACKUP_DIR:-/opt/botmanager-backups}"
KEEP_DAYS="${KEEP_DAYS:-14}"

mkdir -p "$BACKUP_DIR"
cd "$REPO_DIR"

stamp="$(date -u +%Y%m%d-%H%M%S)"
target="$BACKUP_DIR/botmanager-$stamp.sql.gz"

# Custom compose files as used for the deployment (PostgreSQL override is required).
docker compose -f docker-compose.yml -f docker-compose.postgres.yml exec -T db \
  pg_dump -U botmanager -d botmanager --no-owner --clean --if-exists | gzip > "$target"

# Keep the last KEEP_DAYS days.
find "$BACKUP_DIR" -name 'botmanager-*.sql.gz' -mtime +"$KEEP_DAYS" -delete

echo "$(date -u +%FT%TZ) backup written: $target ($(du -h "$target" | cut -f1))"

# Optional off-site copy to OCI Object Storage (Always Free: 20 GB) with the OCI CLI:
#   oci os object put --bucket-name botmanager-backups --file "$target" --name "$(basename "$target")"
