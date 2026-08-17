.PHONY: help db db-down db-logs install backend frontend dev stop test release

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
