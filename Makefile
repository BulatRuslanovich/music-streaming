.PHONY: help db db-down db-logs install backend frontend dev stop

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

db:
	$(COMPOSE_DEV) up -d postgres

db-down:
	$(COMPOSE_DEV) down

db-logs:
	$(COMPOSE_DEV) logs -f postgres

install:
	cd frontend && npm install

backend:
	cd backend/src/MusicStreaming.Api && dotnet run

frontend:
	cd frontend && npm run dev

dev: db
	@trap 'kill 0' EXIT INT TERM; \
	(cd backend/src/MusicStreaming.Api && dotnet run) & \
	(cd frontend && npm run dev) & \
	wait

stop: db-down
