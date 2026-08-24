.PHONY: help db db-down db-logs install backend frontend dev stop test release \
	backup backup-full backups restore backup-pull

COMPOSE_DEV := docker compose -f docker-compose.yml -f docker-compose.dev.yml

help:
	@echo "make db        - поднять postgres в docker (порт на loopback)"
	@echo "make db-down   - остановить postgres"
	@echo "make db-logs   - логи postgres"
	@echo "make install   - npm install для фронта"
	@echo "make backend   - запустить API (dotnet run)"
	@echo "make frontend  - запустить фронт (next dev)"
	@echo "make dev       - поднять db + backend + frontend вместе"
	@echo "make stop      - остановить db"
	@echo "make test      - прогнать тесты бэкенда (нужен docker: базу поднимает сам набор)"
	@echo "make release VERSION=1.1.0 - проставить версию везде и создать тег"
	@echo "make backup    - снапшот базы и storage в ./backups (без hls/transcodes)"
	@echo "make backup-full - то же, но вместе с hls/ и transcodes/"
	@echo "make backups   - список снапшотов"
	@echo "make restore SNAPSHOT=latest - развернуть снапшот (разрушающе)"
	@echo "make backup-pull HOST=user@server - забрать снапшоты с сервера к себе"

db:
	$(COMPOSE_DEV) up -d postgres

db-down:
	$(COMPOSE_DEV) down

db-logs:
	$(COMPOSE_DEV) logs -f postgres

install:
	cd frontend && npm install

backend:
	cd backend/src/MusicStreaming.Api && dotnet watch run

frontend:
	cd frontend && npm run dev

dev: db
	@trap 'kill 0' EXIT INT TERM; \
	(cd backend/src/MusicStreaming.Api && dotnet watch run) & \
	(cd frontend && npm run dev) & \
	wait

stop: db-down

test:
	cd backend && dotnet test MusicStreaming.slnx --configuration Release

release:
	@test -n "$(VERSION)" || (echo "нужна версия: make release VERSION=1.1.0" >&2; exit 1)
	@scripts/release.sh $(VERSION)

backup:
	@scripts/backup.sh

backup-full:
	@scripts/backup.sh --full

backups:
	@ls -1 backups 2>/dev/null || echo "снапшотов пока нет"

restore:
	@test -n "$(SNAPSHOT)" || (echo "нужен снапшот: make restore SNAPSHOT=latest" >&2; exit 1)
	@scripts/restore.sh $(SNAPSHOT)

backup-pull:
	@test -n "$(HOST)" || (echo "нужен хост: make backup-pull HOST=user@server" >&2; exit 1)
	@scripts/backup-pull.sh $(HOST) $(PULL_ARGS)
