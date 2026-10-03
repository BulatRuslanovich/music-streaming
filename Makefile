.PHONY: help db db-down db-reset model install backend frontend test-back test-front \
	fmt-back fmt-front fmt-check lint check

COMPOSE_DEV := docker compose -f docker-compose.yml -f docker-compose.dev.yml
SLN := MusicStreaming.slnx

help:
	@echo "make db          - поднять postgres в docker (порт на loopback)"
	@echo "make db-down     - остановить postgres"
	@echo "make db-reset    - пересоздать базу с нуля по db/init (данные теряются)"
	@echo "make model       - выгрузить модель CLAP в storage/models/clap, если её там ещё нет"
	@echo "make install     - npm install для фронта"
	@echo "make backend     - запустить API (dotnet run)"
	@echo "make frontend    - запустить фронт (next dev)"
	@echo ""
	@echo "make test-back   - тесты бэкенда (нужен docker: базу поднимает сам набор)"
	@echo "make test-front  - тесты фронта (vitest)"
	@echo ""
	@echo "make fmt-back    - dotnet format (whitespace + style)"
	@echo "make fmt-front   - prettier --write"
	@echo "make fmt-check   - проверить форматирование так же, как в CI"
	@echo "make lint        - eslint по фронту"
	@echo "make check       - fmt-check + lint + test (то, что гоняет CI)"

db:
	$(COMPOSE_DEV) up -d postgres

db-down:
	$(COMPOSE_DEV) down

db-reset:
	$(COMPOSE_DEV) rm -sfv postgres
	docker volume rm -f music-streaming_postgres-data
	$(COMPOSE_DEV) up -d postgres

model:
	@test -f storage/models/clap/model.json || $(COMPOSE_DEV) run --rm clap-model

install:
	cd frontend && npm install

backend: model
	cd backend/src/Api && dotnet run

frontend:
	cd frontend && npm run dev

test-back:
	cd backend && dotnet test --solution $(SLN) --configuration Release

test-front:
	cd frontend && npm test

fmt-back:
	cd backend && dotnet format whitespace $(SLN) && dotnet format style $(SLN)

fmt-front:
	cd frontend && npm run format

fmt-check:
	cd backend && dotnet format whitespace $(SLN) --verify-no-changes
	cd backend && dotnet format style $(SLN) --verify-no-changes
	cd frontend && npm run format:check

lint:
	cd frontend && npm run lint

check: fmt-check lint test-back test-front
