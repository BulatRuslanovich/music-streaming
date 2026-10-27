# Зависимости собираются отдельным слоем (cargo-chef): правка кода не пересобирает весь граф крейтов.
FROM rust:1.99-trixie AS chef
RUN cargo install cargo-chef --locked
WORKDIR /src

FROM chef AS planner
COPY . .
RUN cargo chef prepare --recipe-path recipe.json

FROM chef AS build
COPY --from=planner /src/recipe.json recipe.json
RUN cargo chef cook --release --recipe-path recipe.json

COPY . .
# Запросы sqlx проверяются по .sqlx/ — базы при сборке образа нет.
ENV SQLX_OFFLINE=true
RUN cargo build --release --locked --bin caimack

FROM debian:trixie-slim AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates ffmpeg tzdata wget \
    && rm -rf /var/lib/apt/lists/* \
    && test -f /usr/share/zoneinfo/Europe/Moscow

RUN groupadd -r -g 1001 music \
    && useradd -r -u 1001 -g music music \
    && mkdir -p /storage/music /storage/covers \
    && chown -R music:music /storage

COPY --from=build /src/target/release/caimack /usr/local/bin/caimack

USER music
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=4s --start-period=20s --retries=5 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["caimack"]
