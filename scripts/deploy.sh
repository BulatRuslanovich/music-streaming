#!/usr/bin/env bash

set -euo pipefail

die() {
    echo "error: $*" >&2
    exit 1
}

cd "$(dirname "$0")/.." || die "не удалось перейти в корень репозитория"

[ -f .env ] || die "нет .env — скопируйте .env.example и заполните"

if [ $# -gt 1 ]; then
    die "лишние аргументы. Пример: scripts/deploy.sh 1.2.1"
fi

if [ $# -eq 1 ]; then
    version="${1#v}"

    [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$|^latest$ ]] \
        || die "версия '$version' не похожа на X.Y.Z"

    if grep -q '^IMAGE_TAG=' .env; then
        sed -i "s|^IMAGE_TAG=.*|IMAGE_TAG=$version|" .env
    else
        printf '\nIMAGE_TAG=%s\n' "$version" >>.env
    fi

    echo "выкатываем $version"
fi

docker compose pull backend frontend

docker compose up -d --remove-orphans

docker image prune -f >/dev/null

echo
docker compose ps --format 'table {{.Service}}\t{{.Status}}'
