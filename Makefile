.PHONY: db model backend frontend test fmt lint check sqlx apk

db:
	@docker start caimack-rust-dev-db >/dev/null 2>&1 || MSYS_NO_PATHCONV=1 docker run -d --name caimack-rust-dev-db \
		--restart unless-stopped -p 127.0.0.1:55433:5432 \
		-e POSTGRES_USER=music -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=music \
		-v caimack-rust-dev-db:/var/lib/postgresql/data -v "$(CURDIR)/db/init:/docker-entrypoint-initdb.d:ro" \
		postgres:17-alpine

model:
	@test -f storage/models/clap/model.json || docker compose --profile model run --rm clap-model

backend: db model
	cd backend && cargo run

frontend:
	cd frontend && (test -d node_modules || npm install) && npm run dev

test:
	cd backend && cargo test --workspace
	cd frontend && npm test

fmt:
	cd backend && cargo fmt --all
	cd frontend && npm run format

lint:
	cd backend && cargo clippy --workspace --all-targets -- -D warnings
	cd frontend && npm run lint

check: lint test
	cd backend && cargo fmt --all --check
	cd frontend && npm run format:check

sqlx:
	cd backend && cargo sqlx prepare --workspace

apk: ## android/app/build/outputs/apk/release
	cd android && ./gradlew :app:assembleRelease
