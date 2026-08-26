.PHONY: help db db-down db-logs install backend frontend dev stop \
	test test-back test-front test-e2e eval \
	fmt fmt-back fmt-front fmt-check lint headers check release

COMPOSE_DEV := docker compose -f docker-compose.yml -f docker-compose.dev.yml
SLN := MusicStreaming.slnx

help:
	@echo "make db          - поднять postgres в docker (порт на loopback)"
	@echo "make db-down     - остановить postgres"
	@echo "make db-logs     - логи postgres"
	@echo "make install     - npm install для фронта"
	@echo "make backend     - запустить API (dotnet run)"
	@echo "make frontend    - запустить фронт (next dev)"
	@echo "make dev         - поднять db + backend + frontend вместе"
	@echo "make stop        - остановить db"
	@echo ""
	@echo "make test        - тесты бэкенда и фронта"
	@echo "make test-back   - тесты бэкенда (нужен docker: базу поднимает сам набор)"
	@echo "make test-front  - тесты фронта (vitest)"
	@echo "make test-e2e    - e2e-тесты фронта (playwright)"
	@echo "make eval        - оффлайн-оценка рекомендаций: recall@k против базовой линии"
	@echo ""
	@echo "make fmt         - отформатировать всё и проставить SPDX-заголовки"
	@echo "make fmt-back    - dotnet format (whitespace + style)"
	@echo "make fmt-front   - prettier --write"
	@echo "make fmt-check   - проверить форматирование так же, как в CI"
	@echo "make lint        - eslint по фронту"
	@echo "make headers     - проставить недостающие SPDX-заголовки"
	@echo "make check       - fmt-check + lint + test (то, что гоняет CI)"
	@echo ""
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

test: test-back test-front

test-back:
	cd backend && dotnet test --solution $(SLN) --configuration Release

test-front:
	cd frontend && npm test

test-e2e:
	cd frontend && npm run test:e2e

# Метрики печатаются только с -showLiveOutput; без него виден лишь итог «прошло / не прошло».
eval:
	cd backend && dotnet build $(SLN) --configuration Release -v q --nologo && \
		./tests/MusicStreaming.IntegrationTests/bin/Release/net10.0/MusicStreaming.IntegrationTests \
		-filter "/*/*/RecommendationQualityTests/*" -showLiveOutput

fmt: fmt-back fmt-front headers

fmt-back:
	cd backend && dotnet format whitespace $(SLN) && dotnet format style $(SLN)

fmt-front:
	cd frontend && npm run format

fmt-check:
	cd backend && dotnet format whitespace $(SLN) --verify-no-changes
	cd backend && dotnet format style $(SLN) --verify-no-changes
	cd frontend && npm run format:check
	scripts/license-headers.sh --check

lint:
	cd frontend && npm run lint

headers:
	scripts/license-headers.sh

check: fmt-check lint test

release:
	@test -n "$(VERSION)" || (echo "нужна версия: make release VERSION=1.1.0" >&2; exit 1)
	@scripts/release.sh $(VERSION)
