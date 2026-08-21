#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REMOTE="${CONDOTIFY_DEPLOY_REMOTE:-origin}"
BRANCH="${CONDOTIFY_DEPLOY_BRANCH:-feature/ff-access-branding}"
if [[ -z "${CONDOTIFY_IMAGE_REGISTRY+x}" ]] \
    && registry_line="$(grep -m1 -E '^CONDOTIFY_IMAGE_REGISTRY=' "$ROOT_DIR/.env" 2>/dev/null)"; then
    CONDOTIFY_IMAGE_REGISTRY="${registry_line#*=}"
fi
REGISTRY="${CONDOTIFY_IMAGE_REGISTRY:-ghcr.io/lucas78x}"
RELEASE_DIR="$ROOT_DIR/.deploy"
TARGET_SHA="${1:-}"
HEALTH_TIMEOUT="${CONDOTIFY_DEPLOY_HEALTH_TIMEOUT:-180}"

cd "$ROOT_DIR"
mkdir -p "$RELEASE_DIR"
umask 077

current_branch="$(git branch --show-current)"
if [[ "$current_branch" != "$BRANCH" ]]; then
    echo "Branch incorreto: esperado '$BRANCH', atual '$current_branch'." >&2
    exit 2
fi

if [[ -n "$(git status --porcelain)" ]]; then
    echo "Deploy recusado: existem alteracoes Git nao comitadas em $ROOT_DIR." >&2
    git status --short >&2
    exit 3
fi

git fetch --prune "$REMOTE" "$BRANCH"
git merge --ff-only "$REMOTE/$BRANCH"

if [[ -z "$TARGET_SHA" ]]; then
    TARGET_SHA="$(git rev-parse "$REMOTE/$BRANCH")"
fi
TARGET_SHA="$(git rev-parse --verify "${TARGET_SHA}^{commit}")"

if ! git merge-base --is-ancestor "$TARGET_SHA" "$REMOTE/$BRANCH"; then
    echo "Deploy recusado: $TARGET_SHA nao pertence ao historico de $REMOTE/$BRANCH." >&2
    exit 4
fi
if [[ "$(git rev-parse HEAD)" != "$TARGET_SHA" ]]; then
    echo 'Deploy recusado: o checkout deve estar exatamente no commit alvo.' >&2
    exit 4
fi
if [[ -n "$(git status --porcelain)" ]]; then
    echo 'Deploy recusado: o checkout deixou de estar limpo apos a atualizacao.' >&2
    exit 4
fi

target_env="$RELEASE_DIR/release-$TARGET_SHA.env"
previous_env="$RELEASE_DIR/rollback-$TARGET_SHA.env"

cat > "$target_env" <<EOF
CONDOTIFY_API_IMAGE=$REGISTRY/condotify-api:sha-$TARGET_SHA
CONDOTIFY_PORTAL_IMAGE=$REGISTRY/condotify-portal:sha-$TARGET_SHA
CONDOTIFY_LPR_IMAGE=$REGISTRY/condotify-lpr-ocr:sha-$TARGET_SHA
EOF

current_image() {
    local service="$1"
    local fallback="$2"
    local container_id
    container_id="$(docker compose ps -q "$service" 2>/dev/null || true)"
    if [[ -n "$container_id" ]]; then
        docker inspect --format '{{.Config.Image}}' "$container_id"
    else
        printf '%s\n' "$fallback"
    fi
}

cat > "$previous_env" <<EOF
CONDOTIFY_API_IMAGE=$(current_image api condotify-api:local)
CONDOTIFY_PORTAL_IMAGE=$(current_image portal condotify-portal:local)
CONDOTIFY_LPR_IMAGE=$(current_image lpr-ocr condotify-lpr-ocr:local)
EOF

compose_target=(docker compose --env-file .env --env-file "$target_env")
compose_previous=(docker compose --env-file .env --env-file "$previous_env")

"${compose_target[@]}" config --quiet
"${compose_target[@]}" pull api portal lpr-ocr

resolve_digest() {
    local image="$1"
    local revision digest
    revision="$(docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$image")"
    if [[ "$revision" != "$TARGET_SHA" ]]; then
        echo "Imagem recusada: $image declara revision '$revision', esperado '$TARGET_SHA'." >&2
        return 1
    fi
    digest="$(docker image inspect --format '{{index .RepoDigests 0}}' "$image")"
    if [[ -z "$digest" || "$digest" != *@sha256:* ]]; then
        echo "Imagem recusada: nao foi possivel fixar o digest de $image." >&2
        return 1
    fi
    printf '%s\n' "$digest"
}

api_digest="$(resolve_digest "$REGISTRY/condotify-api:sha-$TARGET_SHA")"
portal_digest="$(resolve_digest "$REGISTRY/condotify-portal:sha-$TARGET_SHA")"
lpr_digest="$(resolve_digest "$REGISTRY/condotify-lpr-ocr:sha-$TARGET_SHA")"
cat > "$target_env" <<EOF
CONDOTIFY_API_IMAGE=$api_digest
CONDOTIFY_PORTAL_IMAGE=$portal_digest
CONDOTIFY_LPR_IMAGE=$lpr_digest
EOF

deployment_started=false
database_may_have_changed=false
backup_path=''

wait_for_health() {
    local -a compose_command=("$@")
    local -a health_services=(postgres api portal caddy)
    local deadline=$((SECONDS + HEALTH_TIMEOUT))
    local all_healthy service container_id health

    while (( SECONDS < deadline )); do
        all_healthy=true
        for service in "${health_services[@]}"; do
            container_id="$("${compose_command[@]}" ps -q "$service")"
            if [[ -z "$container_id" ]]; then
                all_healthy=false
                continue
            fi
            health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}missing{{end}}' "$container_id")"
            if [[ "$health" == unhealthy ]]; then
                "${compose_command[@]}" logs --tail=100 "$service" >&2
                return 1
            fi
            [[ "$health" == healthy ]] || all_healthy=false
        done
        [[ "$all_healthy" == true ]] && return 0
        sleep 3
    done
    return 1
}

rollback() {
    local original_status="${1:-$?}"
    trap - ERR INT TERM
    set +e
    if [[ "$deployment_started" == true ]]; then
        echo 'Deploy falhou; iniciando rollback automatico.' >&2
        if [[ "$database_may_have_changed" == true && -n "$backup_path" ]]; then
            CONDOTIFY_ALLOW_DATABASE_RESTORE=yes ./deploy/restore-postgres.sh "$backup_path"
        fi
        "${compose_previous[@]}" up -d --no-build --remove-orphans
        if wait_for_health "${compose_previous[@]}"; then
            echo 'rollback_status=healthy' >&2
        else
            echo 'rollback_status=unhealthy' >&2
            "${compose_previous[@]}" ps >&2
        fi
    fi
    exit "$original_status"
}
trap 'rollback $?' ERR
trap 'rollback 130' INT
trap 'rollback 143' TERM

# As imagens sao baixadas/validadas antes desta janela. Depois interrompemos
# novas escritas, verificamos o backup por restore e migramos uma unica vez.
deployment_started=true
docker compose stop portal api >/dev/null

backup_output="$(./deploy/backup-postgres.sh)"
printf '%s\n' "$backup_output"
backup_path="$(printf '%s\n' "$backup_output" | sed -n 's/^backup_path=//p' | tail -n 1)"
if [[ -z "$backup_path" || ! -f "$backup_path" ]]; then
    echo 'Deploy recusado: o backup verificado nao informou um arquivo valido.' >&2
    false
fi

database_may_have_changed=true
"${compose_target[@]}" --profile ops run --rm --no-deps migration
"${compose_target[@]}" up -d --no-build --remove-orphans

if ! wait_for_health "${compose_target[@]}"; then
    "${compose_target[@]}" ps >&2
    echo 'Os servicos nao ficaram saudaveis dentro do prazo.' >&2
    false
fi

trap - ERR INT TERM
cp "$target_env" "$RELEASE_DIR/current.env"
printf '%s\t%s\t%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$TARGET_SHA" "$backup_path" >> "$RELEASE_DIR/history.tsv"
rm -f -- "$previous_env"

"${compose_target[@]}" ps
printf 'deployment_commit=%s\n' "$TARGET_SHA"
printf 'deployment_images=pinned-by-digest\n'
printf 'rollback_backup=%s\n' "$backup_path"
