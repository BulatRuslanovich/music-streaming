#!/usr/bin/env bash
#
# Разворачивает выпущенную версию на сервере.
#
# Собирать здесь нечего: образы уже лежат в GHCR, их кладёт туда workflow Release по тегу vX.Y.Z.
# Скрипт лишь фиксирует версию в .env, тянет образы и подменяет контейнеры.
#
#   scripts/deploy.sh 1.2.1     конкретная версия
#   scripts/deploy.sh           то, что уже записано в .env
#
# Пересоздаются только backend и frontend: у остальных сервисов ни образ, ни конфигурация не
# меняются, и compose их не трогает — Postgres, Caddy и мониторинг переживают деплой стоя.

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

    # Строка может отсутствовать — тогда дописываем, иначе заменяем на месте.
    if grep -q '^IMAGE_TAG=' .env; then
        sed -i "s|^IMAGE_TAG=.*|IMAGE_TAG=$version|" .env
    else
        printf '\nIMAGE_TAG=%s\n' "$version" >>.env
    fi

    echo "выкатываем $version"
fi

# Образа может не оказаться: тег не тот, workflow ещё идёт, пакет приватный. Провалиться нужно
# здесь, пока старые контейнеры целы, а не на середине подмены.
docker compose pull backend frontend

docker compose up -d --remove-orphans

# Предыдущие образы после подмены висят без тегов и за десяток деплоев съедают заметный объём.
docker image prune -f >/dev/null

echo
docker compose ps --format 'table {{.Service}}\t{{.Status}}'
