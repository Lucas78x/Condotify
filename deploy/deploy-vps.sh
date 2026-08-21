#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REMOTE="${CONDOTIFY_DEPLOY_REMOTE:-origin}"
BRANCH="${CONDOTIFY_DEPLOY_BRANCH:-feature/ff-access-branding}"

cd "$ROOT_DIR"

current_branch="$(git branch --show-current)"
if [[ "$current_branch" != "$BRANCH" ]]; then
    echo "Branch incorreto: esperado '$BRANCH', atual '$current_branch'." >&2
    exit 2
fi

if ! git diff --quiet \
    || ! git diff --cached --quiet \
    || [[ -n "$(git ls-files --others --exclude-standard)" ]]; then
    echo "Deploy recusado: existem alteracoes Git nao comitadas em $ROOT_DIR." >&2
    git status --short >&2
    exit 3
fi

git fetch --prune "$REMOTE" "$BRANCH"
git merge --ff-only "$REMOTE/$BRANCH"

# A atualizacao precisa continuar limpa; arquivos gerados ou alterados pelo
# deploy nunca devem virar estado permanente no checkout de producao.
if [[ -n "$(git status --porcelain)" ]]; then
    echo "Deploy recusado: o checkout deixou de estar limpo apos a atualizacao." >&2
    git status --short >&2
    exit 4
fi

docker compose config --quiet
docker compose up -d --build --remove-orphans --wait --wait-timeout 180

health_services=(postgres api portal caddy)
health_deadline=$((SECONDS + 180))
while (( SECONDS < health_deadline )); do
    all_healthy=true
    for service in "${health_services[@]}"; do
        container_id="$(docker compose ps -q "$service")"
        health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}missing{{end}}' "$container_id")"
        if [[ "$health" == unhealthy ]]; then
            docker compose ps
            docker compose logs --tail=100 "$service"
            echo "Deploy recusado: o servico '$service' ficou unhealthy." >&2
            exit 5
        fi
        [[ "$health" == healthy ]] || all_healthy=false
    done

    [[ "$all_healthy" == true ]] && break
    sleep 3
done

if [[ "$all_healthy" != true ]]; then
    docker compose ps
    echo 'Deploy recusado: os servicos nao ficaram saudaveis dentro do prazo.' >&2
    exit 6
fi

docker compose ps

echo "deployment_commit=$(git rev-parse HEAD)"
