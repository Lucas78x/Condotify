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
docker compose ps

echo "deployment_commit=$(git rev-parse HEAD)"
