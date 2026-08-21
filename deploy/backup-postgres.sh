#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

for setting in \
    CONDOTIFY_POSTGRES_BACKUP_PATH \
    CONDOTIFY_BACKUP_DAILY_RETENTION_DAYS \
    CONDOTIFY_BACKUP_WEEKLY_RETENTION_DAYS \
    CONDOTIFY_BACKUP_MONTHLY_RETENTION_DAYS \
    CONDOTIFY_BACKUP_OFFSITE_REMOTE \
    CONDOTIFY_REQUIRE_OFFSITE_BACKUP; do
    if [[ -z "${!setting+x}" ]] && line="$(grep -m1 -E "^${setting}=" .env 2>/dev/null)"; then
        printf -v "$setting" '%s' "${line#*=}"
        export "$setting"
    fi
done

BACKUP_ROOT="${CONDOTIFY_POSTGRES_BACKUP_PATH:-$ROOT_DIR/backups/postgres}"
DAILY_RETENTION_DAYS="${CONDOTIFY_BACKUP_DAILY_RETENTION_DAYS:-7}"
WEEKLY_RETENTION_DAYS="${CONDOTIFY_BACKUP_WEEKLY_RETENTION_DAYS:-35}"
MONTHLY_RETENTION_DAYS="${CONDOTIFY_BACKUP_MONTHLY_RETENTION_DAYS:-400}"
OFFSITE_REMOTE="${CONDOTIFY_BACKUP_OFFSITE_REMOTE:-}"
REQUIRE_OFFSITE="${CONDOTIFY_REQUIRE_OFFSITE_BACKUP:-false}"

umask 077
mkdir -p "$BACKUP_ROOT/daily" "$BACKUP_ROOT/weekly" "$BACKUP_ROOT/monthly"

exec 9>"$BACKUP_ROOT/.backup.lock"
if ! flock -n 9; then
    echo 'Ja existe um backup PostgreSQL em andamento.' >&2
    exit 2
fi

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
file_name="condotify-$stamp.dump"
temporary="$(mktemp "$BACKUP_ROOT/.${file_name}.XXXXXX")"
verify_db="condotify_verify_${stamp//[^0-9]/}_$$"
verify_created=false

cleanup() {
    rm -f -- "$temporary"
    if [[ "$verify_created" == true ]]; then
        docker compose exec -T postgres dropdb --username postgres --if-exists --force "$verify_db" >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT

docker compose exec -T postgres pg_dump \
    --username postgres \
    --dbname Condotify \
    --format custom \
    --compress 6 \
    --no-owner \
    --no-privileges > "$temporary"

if [[ ! -s "$temporary" ]]; then
    echo 'O pg_dump produziu um arquivo vazio.' >&2
    exit 3
fi

docker compose exec -T postgres pg_restore --list < "$temporary" >/dev/null
docker compose exec -T postgres createdb --username postgres "$verify_db"
verify_created=true
docker compose exec -T postgres pg_restore \
    --username postgres \
    --dbname "$verify_db" \
    --exit-on-error \
    --no-owner \
    --no-privileges < "$temporary" >/dev/null
docker compose exec -T postgres psql \
    --username postgres \
    --dbname "$verify_db" \
    --tuples-only \
    --command 'SELECT current_database();' | grep -Fq "$verify_db"
docker compose exec -T postgres dropdb --username postgres --if-exists --force "$verify_db" >/dev/null
verify_created=false

daily_path="$BACKUP_ROOT/daily/$file_name"
mv -- "$temporary" "$daily_path"
sha256sum "$daily_path" > "$daily_path.sha256"

copy_tier() {
    local tier="$1"
    local target="$BACKUP_ROOT/$tier/$file_name"
    cp --reflink=auto --preserve=mode,timestamps "$daily_path" "$target"
    sha256sum "$target" > "$target.sha256"
}

[[ "$(date -u +%u)" == 7 ]] && copy_tier weekly
[[ "$(date -u +%d)" == 01 ]] && copy_tier monthly

prune_tier() {
    local tier="$1"
    local days="$2"
    find "$BACKUP_ROOT/$tier" -type f -name 'condotify-*.dump' -mtime "+$days" -delete
    find "$BACKUP_ROOT/$tier" -type f -name 'condotify-*.dump.sha256' -mtime "+$days" -delete
}

prune_tier daily "$DAILY_RETENTION_DAYS"
prune_tier weekly "$WEEKLY_RETENTION_DAYS"
prune_tier monthly "$MONTHLY_RETENTION_DAYS"

printf 'backup_path=%s\n' "$daily_path"
printf 'restore_verification=passed\n'

offsite_status=disabled
if [[ -n "$OFFSITE_REMOTE" ]]; then
    if ! command -v rclone >/dev/null 2>&1; then
        echo 'CONDOTIFY_BACKUP_OFFSITE_REMOTE foi definido, mas rclone nao esta instalado.' >&2
        exit 4
    fi

    rclone copyto "$daily_path" "$OFFSITE_REMOTE/daily/$file_name"
    rclone copyto "$daily_path.sha256" "$OFFSITE_REMOTE/daily/$file_name.sha256"
    offsite_status=uploaded
elif [[ "$REQUIRE_OFFSITE" == true ]]; then
    echo 'Backup local verificado, mas o destino externo obrigatorio nao foi configurado.' >&2
    exit 5
fi

printf 'offsite=%s\n' "$offsite_status"
