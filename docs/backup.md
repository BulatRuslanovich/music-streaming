# Backup and restore

Two things have to survive: the PostgreSQL database (accounts, library metadata, playlists,
listening history, recommendation state) and the `storage/` directory (the audio files themselves
plus covers and artist images). Everything else — images, transcodes, Prometheus and Loki data — is
either pulled from a registry or regenerated.

A backup is a **snapshot directory** under `backups/`:

```
backups/
├── 2026-08-24_043012/
│   ├── db.dump          # pg_dump custom format, compressed, no owners/ACLs
│   ├── manifest.txt     # timestamp, app version, checksum, what is inside
│   └── storage/         # music/, covers/, artists/, playlists/
├── 2026-08-24_133045/
└── latest -> 2026-08-24_133045
```

Each snapshot looks complete, but `storage/` is copied with `rsync --link-dest`: files that did not
change since the previous snapshot become hard links to it. Ten nightly snapshots of a 300 GB
library cost roughly 300 GB plus whatever was added, not 3 TB.

`hls/` and `transcodes/` are **excluded by default**. They are derived from the originals and the
transcode backfill worker rebuilds them in the background after a restore. On a typical library they
are about as large as the music itself, so skipping them halves the backup. Use `--full` if you would
rather not wait for the rebuild.

## Taking a backup

On the server, from the project directory:

```bash
scripts/backup.sh              # or: make backup
scripts/backup.sh --full       # include hls/ and transcodes/
scripts/backup.sh --db-only    # just the database
scripts/backup.sh --keep 30    # keep 30 snapshots instead of BACKUP_KEEP
```

PostgreSQL must be running (`docker compose up -d postgres`); the dump is taken from inside the
container, so the database credentials are never read from `.env`. The dump is consistent — `pg_dump`
runs in a single transaction — so there is no need to stop the application first.

Retention, location and the derived-data default come from `.env`:

```env
BACKUP_DIR=./backups
BACKUP_KEEP=7
BACKUP_INCLUDE_DERIVED=false
```

An interrupted run leaves nothing behind: the snapshot is assembled in `.incomplete-*` and renamed
into place only when it is complete. Concurrent runs are prevented with a lock file.

## Scheduling

`deploy/caimack-backup.service` and `deploy/caimack-backup.timer` run a snapshot at 04:30 every
night. Edit `User=` and `WorkingDirectory=` first:

```bash
sudo cp deploy/caimack-backup.service deploy/caimack-backup.timer /etc/systemd/system/
sudoedit /etc/systemd/system/caimack-backup.service
sudo systemctl daemon-reload
sudo systemctl enable --now caimack-backup.timer

systemctl list-timers caimack-backup.timer
journalctl -u caimack-backup.service -n 50
```

With cron instead:

```cron
30 4 * * * cd /srv/music-streaming && scripts/backup.sh >> /var/log/caimack-backup.log 2>&1
```

## Pulling backups to your own machine

Snapshots on the same disk as the server protect against a bad migration, not against losing the
server. From your laptop, in a clone of this repository:

```bash
scripts/backup-pull.sh me@server                          # all snapshots
scripts/backup-pull.sh me@server --run-backup             # take a fresh one first, then pull
scripts/backup-pull.sh me@server --snapshot latest        # only the newest
scripts/backup-pull.sh me@server --repo /srv/music-streaming --port 2222
```

The transfer is `rsync -aH`, so it resumes after a dropped connection, only sends what changed since
the last pull, and preserves the hard links between snapshots — the local copy stays as compact as
the remote one.

Nothing is deleted locally: snapshots the server has already rotated out stay in your copy until you
remove them by hand. `BACKUP_KEEP` on the server does not shorten your own history.

## Restoring

```bash
scripts/restore.sh latest              # or: make restore SNAPSHOT=latest
scripts/restore.sh 2026-08-24_043012
scripts/restore.sh latest --db-only
scripts/restore.sh /media/usb/backups/2026-08-24_043012
```

The script prints what it is about to do and asks for confirmation (`--yes` to skip, required when
there is no terminal). Then it:

1. verifies `db.dump` against the SHA-256 in the manifest,
2. stops `backend` and `frontend` so nothing writes during the restore,
3. drops schema `public` and restores the dump into the empty schema in a single transaction,
4. brings `storage/` to the snapshot state with `rsync --delete` and fixes ownership to `PUID:PGID`,
5. starts the stack again (`--no-start` to skip).

This is destructive: anything created after the snapshot is gone. If the snapshot has no
`hls/`/`transcodes/`, whatever is already on disk is left alone — stale entries are keyed by content
hash and simply go unused.

## Moving to another server

```bash
# 1. on the old server — a fresh, complete snapshot
scripts/backup.sh

# 2. on your machine — pull it
scripts/backup-pull.sh me@old-server --snapshot latest

# 3. on the new server — the project and its configuration
git clone https://github.com/BulatRuslanovich/music-streaming.git /srv/music-streaming
scp .env me@new-server:/srv/music-streaming/.env

# 4. push the snapshot up
rsync -aH --info=progress2 backups/2026-08-24_043012 me@new-server:/srv/music-streaming/backups/

# 5. on the new server
cd /srv/music-streaming
scripts/restore.sh 2026-08-24_043012
```

Steps 2 and 4 can be one `rsync` between the servers if they can reach each other directly.

Notes on the configuration:

- **Copy `.env` across, do not regenerate it.** `JWT_SIGNING_KEY` signs the cookies of logged-in
  sessions; a new key logs everyone out. `OWNER_PASSWORD` is only used to seed the owner account on
  an empty database, so it has no effect on a restore.
- `POSTGRES_USER`/`POSTGRES_PASSWORD` may differ between servers. The dump is taken with
  `--no-owner --no-acl`, so it restores under whatever role the new database uses.
- Point DNS at the new server before or right after the restore. Caddy issues a certificate for
  `PUBLIC_DOMAIN` on first start, which needs the domain to resolve to it.
- Only then shut the old server down.

## Checking a backup is real

A backup nobody has restored is a hypothesis. The cheap version of the test, on any machine with
Docker:

```bash
docker run -d --name check -e POSTGRES_PASSWORD=x -e POSTGRES_USER=music -e POSTGRES_DB=music postgres:17-alpine
docker exec -i check pg_restore -U music -d music --no-owner --no-acl < backups/latest/db.dump
docker exec -i check psql -U music -d music -c 'select count(*) from tracks'
docker rm -f check
```
