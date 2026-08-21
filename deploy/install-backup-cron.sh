#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MARKER='# condotify-postgres-backup'
SCHEDULE="${CONDOTIFY_BACKUP_CRON_SCHEDULE:-15 2 * * *}"
LOG_PATH="$ROOT_DIR/backups/postgres/backup.log"

command -v crontab >/dev/null 2>&1 || {
    echo 'crontab nao esta instalado na VPS.' >&2
    exit 2
}

mkdir -p "$(dirname "$LOG_PATH")"
current="$(crontab -l 2>/dev/null || true)"
filtered="$(printf '%s\n' "$current" | grep -Fv "$MARKER" || true)"
entry="$SCHEDULE cd $ROOT_DIR && ./deploy/backup-postgres.sh >> $LOG_PATH 2>&1 $MARKER"
printf '%s\n%s\n' "$filtered" "$entry" | sed '/^[[:space:]]*$/d' | crontab -

printf 'backup_cron_installed=%s\n' "$entry"
