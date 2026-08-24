#!/usr/bin/env bash

set -euo pipefail

die() {
    echo "error: $*" >&2
    exit 1
}

usage() {
    cat <<'EOF'
scripts/backup-pull.sh <user@host> [опции] — забрать снапшоты с сервера к себе

  --repo PATH        каталог проекта на сервере (по умолчанию ~/music-streaming)
  --remote-dir PATH  каталог со снапшотами (по умолчанию <repo>/backups)
  --dest DIR         куда класть локально (по умолчанию BACKUP_DIR или ./backups)
  --snapshot NAME    забрать один снапшот ('latest' или имя), а не всё
  --run-backup       сначала сделать свежий снапшот на сервере
  --port N           ssh-порт
  --dry-run          показать, что было бы скачано
  -h, --help         эта справка

Качает rsync-ом с -H: жёсткие ссылки между снапшотами сохраняются, так что
десяток снапшотов локально занимает примерно как один плюс дельты.
EOF
}

remote=""
repo="music-streaming"
remote_dir=""
dest=""
snapshot=""
run_backup=false
ssh_port=""
dry_run=false

while [ $# -gt 0 ]; do
    case "$1" in
    --repo)
        repo="${2:?--repo требует путь}"
        shift
        ;;
    --remote-dir)
        remote_dir="${2:?--remote-dir требует путь}"
        shift
        ;;
    --dest)
        dest="${2:?--dest требует каталог}"
        shift
        ;;
    --snapshot)
        snapshot="${2:?--snapshot требует имя}"
        shift
        ;;
    --run-backup) run_backup=true ;;
    --port)
        ssh_port="${2:?--port требует номер}"
        shift
        ;;
    --dry-run) dry_run=true ;;
    -h | --help)
        usage
        exit 0
        ;;
    -*) die "неизвестный аргумент '$1' (--help)" ;;
    *)
        [ -z "$remote" ] || die "лишний аргумент '$1'"
        remote="$1"
        ;;
    esac
    shift
done

[ -n "$remote" ] || {
    usage
    exit 1
}

command -v rsync >/dev/null 2>&1 || die "нужен rsync"
command -v ssh >/dev/null 2>&1 || die "нужен ssh"

cd "$(dirname "$0")/.." || die "не удалось перейти в корень репозитория"

if [ -z "$dest" ]; then
    dest="./backups"
    if [ -f .env ]; then
        line="$(grep -E '^[[:space:]]*BACKUP_DIR=' .env | tail -1 || true)"
        [ -n "$line" ] && dest="${line#*=}"
    fi
fi

[ -n "$remote_dir" ] || remote_dir="$repo/backups"

ssh_cmd=(ssh)
[ -n "$ssh_port" ] && ssh_cmd+=(-p "$ssh_port")

if $run_backup; then
    echo "==> делаем снапшот на $remote"
    "${ssh_cmd[@]}" "$remote" "cd '$repo' && scripts/backup.sh"
    echo
fi

mkdir -p "$dest"

rsync_opts=(-aH --partial --info=progress2 --human-readable)
[ -n "$ssh_port" ] && rsync_opts+=(-e "ssh -p $ssh_port")
$dry_run && rsync_opts+=(--dry-run)

if [ -n "$snapshot" ]; then
    # latest — симлинк на сервере, разворачиваем в реальное имя, чтобы локально
    # лёг нормальный каталог, а не битая ссылка.
    if [ "$snapshot" = "latest" ]; then
        snapshot="$("${ssh_cmd[@]}" "$remote" "readlink '$remote_dir/latest'" | tr -d '\r\n')"
        [ -n "$snapshot" ] || die "на сервере нет $remote_dir/latest"
        echo "latest → $snapshot"
    fi
    echo "==> качаем $snapshot"
    rsync "${rsync_opts[@]}" "$remote:$remote_dir/$snapshot/" "${dest%/}/$snapshot/"
else
    echo "==> качаем все снапшоты из $remote:$remote_dir"
    rsync "${rsync_opts[@]}" \
        --exclude='.lock' --exclude='.incomplete-*' \
        "$remote:${remote_dir%/}/" "${dest%/}/"
fi

echo
echo "локальная копия: $(cd "$dest" && pwd)"
find "$dest" -mindepth 1 -maxdepth 1 -type d -name '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_*' |
    sort | while read -r dir; do
    printf '  %-22s %s\n' "$(basename "$dir")" "$(du -sh "$dir" | cut -f1)"
done
echo
echo "поднять из него на новом сервере: scripts/restore.sh <имя снапшота>"
