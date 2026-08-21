#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -z "${CONDOTIFY_POSTGRES_BACKUP_PATH+x}" ]] \
    && line="$(grep -m1 -E '^CONDOTIFY_POSTGRES_BACKUP_PATH=' "$ROOT_DIR/.env" 2>/dev/null)"; then
    CONDOTIFY_POSTGRES_BACKUP_PATH="${line#*=}"
fi
BACKUP_ROOT="${CONDOTIFY_POSTGRES_BACKUP_PATH:-$ROOT_DIR/backups/postgres}"
backup_path="${1:-}"

if [[ "${CONDOTIFY_ALLOW_DATABASE_RESTORE:-}" != yes ]]; then
    echo 'Restore recusado: defina CONDOTIFY_ALLOW_DATABASE_RESTORE=yes para confirmar.' >&2
    exit 2
fi
if [[ -z "$backup_path" ]]; then
    echo "Uso: CONDOTIFY_ALLOW_DATABASE_RESTORE=yes $0 <arquivo.dump>" >&2
    exit 2
fi

cd "$ROOT_DIR"
resolved_root="$(realpath "$BACKUP_ROOT")"
resolved_backup="$(realpath "$backup_path")"
if [[ "$resolved_backup" != "$resolved_root"/* || ! -f "$resolved_backup" ]]; then
    echo 'Restore recusado: o arquivo precisa pertencer ao diretorio oficial de backups.' >&2
    exit 3
fi

checksum="$resolved_backup.sha256"
if [[ ! -f "$checksum" ]]; then
    echo 'Restore recusado: checksum ausente.' >&2
    exit 4
fi
(cd "$(dirname "$resolved_backup")" && sha256sum --check "$(basename "$checksum")")
docker compose exec -T postgres pg_restore --list < "$resolved_backup" >/dev/null

docker compose stop portal api mediamtx >/dev/null
docker compose exec -T postgres dropdb --username postgres --if-exists --force Condotify
docker compose exec -T postgres createdb --username postgres Condotify
docker compose exec -T postgres pg_restore \
    --username postgres \
    --dbname Condotify \
    --exit-on-error \
    --no-owner \
    --no-privileges < "$resolved_backup"

printf 'database_restore=completed\n'
printf 'restored_from=%s\n' "$resolved_backup"
