#!/usr/bin/env bash

set -euo pipefail

die() {
    echo "error: $*" >&2
    exit 1
}

usage() {
    cat <<'EOF'
scripts/backup.sh [опции] — снапшот базы и storage на сервере

  --full          положить в снапшот hls/ и transcodes/ (по умолчанию нет:
                  они пересобираются из оригиналов бэкфиллом транскодера)
  --db-only       только дамп базы, без storage
  --keep N        сколько снапшотов оставить (по умолчанию BACKUP_KEEP или 7)
  --out DIR       куда складывать (по умолчанию BACKUP_DIR или ./backups)
  -h, --help      эта справка

Снапшот — каталог backups/<дата>/ с db.dump, storage/ и manifest.txt.
Storage переносится через rsync --link-dest: неизменившиеся файлы становятся
жёсткими ссылками на предыдущий снапшот, так что каждый снапшот выглядит полным,
а место занимает только дельту.
EOF
}

cd "$(dirname "$0")/.." || die "не удалось перейти в корень репозитория"

[ -f .env ] || die "нет .env — скопируйте .env.example и заполните"

# Значение из .env: последнее вхождение, без кавычек и хвостового комментария.
# Годится только для несекретных ключей — пароль с '#' внутри тут обрежется,
# поэтому учётные данные базы берём из окружения самого контейнера, а не отсюда.
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

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d' ' -f1
    else
        shasum -a 256 "$1" | cut -d' ' -f1
    fi
}

include_derived=false
db_only=false
backup_dir="$(env_value BACKUP_DIR ./backups)"
keep="$(env_value BACKUP_KEEP 7)"
[ "$(env_value BACKUP_INCLUDE_DERIVED false)" = "true" ] && include_derived=true

while [ $# -gt 0 ]; do
    case "$1" in
    --full) include_derived=true ;;
    --db-only) db_only=true ;;
    --keep)
        keep="${2:?--keep требует число}"
        shift
        ;;
    --out)
        backup_dir="${2:?--out требует каталог}"
        shift
        ;;
    -h | --help)
        usage
        exit 0
        ;;
    *) die "неизвестный аргумент '$1' (--help)" ;;
    esac
    shift
done

[[ "$keep" =~ ^[0-9]+$ ]] || die "--keep должно быть числом, получено '$keep'"

storage_path="$(env_value MUSIC_STORAGE_PATH ./storage)"

command -v docker >/dev/null 2>&1 || die "нужен docker"
$db_only || command -v rsync >/dev/null 2>&1 || die "нужен rsync (apt install rsync)"

$db_only || [ -d "$storage_path" ] || die "нет каталога storage: $storage_path"

docker compose exec -T postgres pg_isready -q >/dev/null 2>&1 \
    || die "postgres недоступен — сначала docker compose up -d postgres"

mkdir -p "$backup_dir"

# Cron может запустить второй бекап поверх идущего — не даём.
if command -v flock >/dev/null 2>&1; then
    exec 9>"$backup_dir/.lock"
    flock -n 9 || die "другой бекап уже идёт"
fi

stamp="$(date -u +%Y-%m-%d_%H%M%S)"
snapshot="$backup_dir/$stamp"
staging="$backup_dir/.incomplete-$stamp"

[ -e "$snapshot" ] && die "снапшот $snapshot уже существует"

rm -rf "$staging"
mkdir -p "$staging"
# Снапшот появляется под своим именем только целиком — оборванный не переживёт выход.
trap 'rm -rf "$staging"' EXIT

echo "==> дамп базы"
docker compose exec -T postgres sh -c \
    'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --compress=6 --no-owner --no-acl' \
    >"$staging/db.dump"

[ -s "$staging/db.dump" ] || die "pg_dump вернул пустой файл"

db_bytes="$(wc -c <"$staging/db.dump" | tr -d ' ')"
db_sha="$(sha256_of "$staging/db.dump")"
echo "    db.dump — $(numfmt --to=iec --suffix=B "$db_bytes" 2>/dev/null || echo "$db_bytes B")"

storage_bytes=0
storage_files=0

if ! $db_only; then
    echo "==> storage"

    link_dest=()
    prev="$(find "$backup_dir" -mindepth 1 -maxdepth 1 -type d \
        -name '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_*' | sort | tail -1)"
    if [ -n "$prev" ] && [ -d "$prev/storage" ]; then
        link_dest=(--link-dest="$(cd "$prev/storage" && pwd)")
        echo "    инкремент поверх $(basename "$prev")"
    fi

    excludes=()
    $include_derived || excludes=(--exclude=/hls/ --exclude=/transcodes/)

    rsync -a --delete "${excludes[@]}" "${link_dest[@]}" \
        "${storage_path%/}/" "$staging/storage/"

    storage_bytes="$(du -sb "$staging/storage" | cut -f1)"
    storage_files="$(find "$staging/storage" -type f | wc -l | tr -d ' ')"
    echo "    $storage_files файл(ов), $(numfmt --to=iec --suffix=B "$storage_bytes" 2>/dev/null || echo "$storage_bytes B")"
fi

app_version="$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' backend/Directory.Build.props 2>/dev/null | head -1)"

cat >"$staging/manifest.txt" <<EOF
created_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)
app_version=${app_version:-unknown}
image_tag=$(env_value IMAGE_TAG latest)
db_dump_sha256=$db_sha
db_dump_bytes=$db_bytes
storage_source=$(cd "$storage_path" 2>/dev/null && pwd || echo "$storage_path")
storage_bytes=$storage_bytes
storage_files=$storage_files
storage_included=$($db_only && echo false || echo true)
derived_included=$($include_derived && echo true || echo false)
host=$(hostname)
EOF

mv "$staging" "$snapshot"
trap - EXIT

ln -sfn "$stamp" "$backup_dir/latest"

if [ "$keep" -gt 0 ]; then
    mapfile -t all < <(find "$backup_dir" -mindepth 1 -maxdepth 1 -type d \
        -name '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_*' | sort)
    total=${#all[@]}
    if [ "$total" -gt "$keep" ]; then
        for old in "${all[@]:0:$((total - keep))}"; do
            echo "==> удаляем старый снапшот $(basename "$old")"
            rm -rf "$old"
        done
    fi
fi

echo
echo "снапшот готов: $snapshot"
echo "на диске занято: $(du -sh "$backup_dir" | cut -f1) всего в $backup_dir"
echo
echo "забрать к себе:  scripts/backup-pull.sh <user>@<host>"
echo "восстановить:    scripts/restore.sh $stamp"
