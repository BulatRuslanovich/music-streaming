#!/usr/bin/env bash

set -euo pipefail

die() {
    echo "error: $*" >&2
    exit 1
}

usage() {
    cat <<'EOF'
scripts/restore.sh <снапшот> [опции] — развернуть снапшот поверх этой установки

  <снапшот>       имя внутри backups/ ('latest', '2026-08-24_120000') или путь

  --db-only       восстановить только базу
  --storage-only  восстановить только файлы
  --keep-running  не останавливать backend/frontend (по умолчанию останавливаем,
                  иначе приложение пишет в базу прямо во время наката)
  --no-start      не поднимать стек в конце
  -y, --yes       не спрашивать подтверждение
  -h, --help      эта справка

Операция разрушающая: схема public в базе дропается целиком, storage
приводится к состоянию снапшота (rsync --delete).
EOF
}

cd "$(dirname "$0")/.." || die "не удалось перейти в корень репозитория"

[ -f .env ] || die "нет .env — скопируйте .env.example и заполните (пароли и ключи должны быть теми же, что на старом сервере)"

env_value() {
    local key="$1" default="${2-}" line
    line="$(grep -E "^[[:space:]]*${key}=" .env | tail -1 || true)"
    [ -n "$line" ] || {
        printf '%s' "$default"
        return
    }
    line="${line#*=}"
    line="${line%%[[:space:]]#*}"
    line="${line#"${line%%[![:space:]]*}"}"
    line="${line%"${line##*[![:space:]]}"}"
    line="${line%\"}"
    line="${line#\"}"
    line="${line%\'}"
    line="${line#\'}"
    printf '%s' "${line:-$default}"
}

manifest_value() {
    local key="$1"
    sed -n "s|^${key}=||p" "$snapshot/manifest.txt" | tail -1
}

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d' ' -f1
    else
        shasum -a 256 "$1" | cut -d' ' -f1
    fi
}

target=""
do_db=true
do_storage=true
assume_yes=false
keep_running=false
start_stack=true

while [ $# -gt 0 ]; do
    case "$1" in
    --db-only) do_storage=false ;;
    --storage-only) do_db=false ;;
    --keep-running) keep_running=true ;;
    --no-start) start_stack=false ;;
    -y | --yes) assume_yes=true ;;
    -h | --help)
        usage
        exit 0
        ;;
    -*) die "неизвестный аргумент '$1' (--help)" ;;
    *)
        [ -z "$target" ] || die "лишний аргумент '$1'"
        target="$1"
        ;;
    esac
    shift
done

[ -n "$target" ] || {
    usage
    exit 1
}

backup_dir="$(env_value BACKUP_DIR ./backups)"
storage_path="$(env_value MUSIC_STORAGE_PATH ./storage)"
puid="$(env_value PUID 1000)"
pgid="$(env_value PGID 1000)"

if [ -d "$target" ]; then
    snapshot="$target"
elif [ -d "$backup_dir/$target" ]; then
    snapshot="$backup_dir/$target"
else
    die "снапшот '$target' не найден ни как путь, ни в $backup_dir"
fi
snapshot="$(cd "$snapshot" && pwd)"

[ -f "$snapshot/manifest.txt" ] || die "в $snapshot нет manifest.txt — это не снапшот"

command -v docker >/dev/null 2>&1 || die "нужен docker"

storage_included="$(manifest_value storage_included)"
derived_included="$(manifest_value derived_included)"

if $do_db && [ ! -f "$snapshot/db.dump" ]; then
    die "в снапшоте нет db.dump"
fi
if $do_storage && [ "$storage_included" != "true" ]; then
    echo "внимание: снапшот сделан с --db-only, файлы восстанавливать нечего" >&2
    do_storage=false
fi
$do_db || $do_storage || die "нечего восстанавливать"

echo "снапшот:     $snapshot"
echo "создан:      $(manifest_value created_at)"
echo "версия:      $(manifest_value app_version) (образы $(manifest_value image_tag))"
echo "источник:    $(manifest_value host):$(manifest_value storage_source)"
$do_db && echo "база:        db.dump, $(manifest_value db_dump_bytes) байт"
$do_storage && echo "файлы:       $(manifest_value storage_files) шт., hls/transcodes $([ "$derived_included" = "true" ] && echo включены || echo "не включены — пересоберутся бэкфиллом")"
echo
echo "цель:        $(pwd)"
$do_db && echo "  база       — схема public будет удалена и залита заново"
$do_storage && echo "  storage    — $storage_path будет приведён к состоянию снапшота"
echo

if ! $assume_yes; then
    [ -t 0 ] || die "нет терминала для подтверждения — запускайте с --yes"
    printf 'это уничтожит текущие данные. Введите yes для продолжения: '
    read -r reply
    [ "$reply" = "yes" ] || die "отменено"
fi

if $do_db; then
    echo "==> проверяем контрольную сумму дампа"
    expected="$(manifest_value db_dump_sha256)"
    actual="$(sha256_of "$snapshot/db.dump")"
    [ "$expected" = "$actual" ] || die "db.dump побился: в манифесте $expected, посчитали $actual"
fi

if ! $keep_running; then
    echo "==> останавливаем backend и frontend"
    docker compose stop backend frontend >/dev/null 2>&1 || true
fi

if $do_db; then
    echo "==> поднимаем postgres"
    docker compose up -d postgres >/dev/null

    for _ in $(seq 1 60); do
        docker compose exec -T postgres pg_isready -q >/dev/null 2>&1 && break
        sleep 1
    done
    docker compose exec -T postgres pg_isready -q >/dev/null 2>&1 \
        || die "postgres так и не поднялся"

    echo "==> чистим схему"
    # Дроп схемы вместо pg_restore --clean: чистая база восстанавливается без
    # шума из ошибок DROP на несуществующих объектах, а расширения (pg_trgm)
    # приезжают из самого дампа.
    docker compose exec -T -e PGOPTIONS='-c client_min_messages=warning' postgres sh -c \
        'psql -v ON_ERROR_STOP=1 -q -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
            -c "DROP SCHEMA IF EXISTS public CASCADE" \
            -c "CREATE SCHEMA public"'

    echo "==> заливаем дамп"
    docker compose exec -T postgres sh -c \
        'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --no-acl --single-transaction --exit-on-error' \
        <"$snapshot/db.dump"
fi

if $do_storage; then
    echo "==> раскладываем файлы"
    command -v rsync >/dev/null 2>&1 || die "нужен rsync (apt install rsync)"

    mkdir -p "$storage_path"

    excludes=()
    # Снапшот без hls/transcodes не должен снести их на месте: они привязаны к
    # хэшу содержимого, лишние просто не будут востребованы.
    [ "$derived_included" = "true" ] || excludes=(--exclude=/hls/ --exclude=/transcodes/)

    rsync -a --delete "${excludes[@]}" "$snapshot/storage/" "${storage_path%/}/"

    echo "==> выставляем владельца $puid:$pgid"
    docker compose run --rm --no-deps -u 0 --entrypoint sh storage-init \
        -c "chown -R $puid:$pgid /storage" >/dev/null
fi

if $start_stack; then
    echo "==> поднимаем стек"
    docker compose up -d --remove-orphans

    echo
    docker compose ps --format 'table {{.Service}}\t{{.Status}}'
fi

echo
echo "восстановлено из $(basename "$snapshot")"
[ "$derived_included" = "true" ] || echo "hls/transcodes пересоберутся фоном (TRANSCODE_BACKFILL_ENABLED)"
