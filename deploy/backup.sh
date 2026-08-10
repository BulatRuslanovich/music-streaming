#!/usr/bin/env bash
# ==============================================================================================
# Backup for the personal music service.
#
# The database and the music files are backed up separately, as the specification requires: the
# database is small and changes constantly, while the audio files are large and effectively
# append-only. Treating them alike would mean copying the whole library to capture a new playlist.
#
#   ./deploy/backup.sh            # database + configuration, and rsync the music tree
#   ./deploy/backup.sh --db-only  # database + configuration only
#
# Nothing here interrupts playback: pg_dump takes a consistent snapshot without locking readers,
# and the music files are copied with rsync while the API keeps them open for streaming (they are
# opened with FileShare.Read for exactly this reason).
# ==============================================================================================

set -euo pipefail

cd "$(dirname "$0")/.."

DB_ONLY=false
[[ "${1:-}" == "--db-only" ]] && DB_ONLY=true

# shellcheck disable=SC1091
[[ -f .env ]] && set -a && source .env && set +a

BACKUP_ROOT="${BACKUP_PATH:-./backups}"
MUSIC_PATH="${MUSIC_STORAGE_PATH:-./storage}"
POSTGRES_USER="${POSTGRES_USER:-music}"
POSTGRES_DB="${POSTGRES_DB:-music}"
STAMP="$(date +%Y-%m-%d_%H%M%S)"
TARGET="${BACKUP_ROOT}/${STAMP}"

mkdir -p "$TARGET"

echo "==> Backing up to ${TARGET}"

# --- database ---------------------------------------------------------------------------------
# The custom format is compressed and restorable selectively with pg_restore.
echo "--> PostgreSQL dump"
docker compose exec -T postgres \
    pg_dump --username="$POSTGRES_USER" --dbname="$POSTGRES_DB" --format=custom --no-owner \
    > "${TARGET}/database.dump"

echo "    $(du -h "${TARGET}/database.dump" | cut -f1) written"

# --- configuration ----------------------------------------------------------------------------
# .env holds the signing key and the database password: without it a restored dump is unusable,
# since every session token would be invalid and the API could not connect.
echo "--> Configuration"
for file in .env docker-compose.yml deploy/Caddyfile; do
    [[ -f "$file" ]] && install -D "$file" "${TARGET}/config/${file}"
done

# --- music files ------------------------------------------------------------------------------
if [[ "$DB_ONLY" == false ]]; then
    echo "--> Music and cover art (incremental)"

    # A single mirror directory reused across runs, with hard links to the previous snapshot: only
    # changed files consume new space, so keeping history does not multiply a 1 TB library.
    MIRROR="${BACKUP_ROOT}/music-mirror"
    mkdir -p "$MIRROR"

    rsync --archive --delete --human-readable --info=stats1 \
        "${MUSIC_PATH}/" "${MIRROR}/"

    ln -sfn "$(cd "$MIRROR" && pwd)" "${TARGET}/music-mirror"
else
    echo "--> Skipping music files (--db-only)"
fi

# --- retention --------------------------------------------------------------------------------
# Keep the most recent snapshots; the music mirror is shared and never pruned here.
KEEP="${BACKUP_KEEP:-14}"
mapfile -t OLD < <(find "$BACKUP_ROOT" -maxdepth 1 -type d -name '20*' | sort -r | tail -n +"$((KEEP + 1))")

if ((${#OLD[@]} > 0)); then
    echo "--> Removing ${#OLD[@]} snapshot(s) older than the last ${KEEP}"
    printf '%s\n' "${OLD[@]}" | xargs -r rm -rf
fi

echo "==> Done."
echo
echo "Restore the database with:"
echo "  docker compose exec -T postgres pg_restore --username=${POSTGRES_USER} \\"
echo "      --dbname=${POSTGRES_DB} --clean --if-exists < ${TARGET}/database.dump"
echo
echo "Restore the music files with:"
echo "  rsync --archive ${BACKUP_ROOT}/music-mirror/ ${MUSIC_PATH}/"
