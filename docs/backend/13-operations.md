# 13. Сборка и эксплуатация

## Образ

[`backend/Dockerfile`](../../backend/Dockerfile) — три стадии.

### `build` — `sdk:10.0-alpine`

Сначала копируются **только** `.csproj`, `Directory.Build.props` и `.slnx`, затем идёт `restore`, и
лишь потом копируется `src/`. Это классический и очень хитрый приём кэширования слоёв: правка кода не приводит к
повторному восстановлению пакетов. 

### `tools` — `runtime:10.0-alpine`

Утилита изображений исполнителей, **отдельной целью**. Она запускается вручную и раз в жизни, а образ
API работает круглосуточно: класть в него то, что почти никогда не исполняется, значит расширять
поверхность без выгоды. Собирается только по явному `--target tools`.

### `runtime` — `aspnet:10.0-alpine` (по умолчанию)

```dockerfile
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_gcServer=0
RUN apk add --no-cache krb5-libs ffmpeg
```

| Решение | Почему |
|---|---|
| `krb5-libs` | Npgsql тянет krb5 даже там, где Kerberos не используется |
| `ffmpeg` | Перекодирование. Единственная внешняя программа, которая нужна приложению |
| `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` | Образ без ICU меньше. **Следствие:** приложение не знает часовых поясов — их знает только Postgres. Отсюда `AT TIME ZONE` в статистике |
| `DOTNET_gcServer=0` | Рабочий GC вместо серверного: на маленькой машине он экономнее по памяти |
| `USER music` (uid/gid 1001) | Приложение не работает от root |
| `HEALTHCHECK` | `/health`, интервал 15 с, стартовый период 40 с, 5 попыток |

## Compose

[`docker-compose.yml`](../../docker-compose.yml) — девять сервисов в одной сети `internal`.

| Сервис | Порты наружу | Роль |
|---|---|---|
| `postgres` | **нет** | `postgres:17-alpine`, `pg_isready`, том `postgres-data` |
| `storage-init` | — | Разовый: создаёт каталоги и выставляет владельца `PUID:PGID` |
| `backend` | `127.0.0.1:${BACKEND_PORT:-8080}` | Только петля |
| `artist-images` | — | Профиль `tools`, вручную |
| `frontend` | нет | Ждёт `backend` в состоянии healthy |
| `caddy` | `${HTTP_PORT:-80}`, `${HTTPS_PORT:-443}` | **Единственная точка входа** |
| `prometheus`, `loki`, `promtail` | нет | Наблюдаемость |
| `grafana` | `127.0.0.1:${GRAFANA_PORT:-3001}` | Только петля |

Порядок запуска бэкенда:

```yaml
depends_on:
  postgres:
    condition: service_healthy
  storage-init:
    condition: service_completed_successfully
```

Даже при этом миграции обёрнуты в цикл повторов — `service_healthy` у Postgres не всегда означает
готовность принимать соединения ([ADR-0011](adr/0011-migrate-on-startup.md)).

**Тома:** `postgres-data`, `caddy-data`, `caddy-config`, `prometheus-data`, `grafana-data`,
`loki-data`. Хранилище музыки — не именованный том, а привязка каталога хоста
(`${MUSIC_STORAGE_PATH:-./storage}:/storage`), чтобы владелец мог работать с файлами напрямую.

### Caddy

[`deploy/Caddyfile`](../../deploy/Caddyfile):

```
handle /api/*    → backend:8080  (flush_interval -1, header_up X-Real-IP)
handle /health   → backend:8080
handle           → frontend:3000
```

Три следствия, о которых нужно помнить:

1. **`/metrics`, `/docs`, `/openapi/*` наружу не попадают** — это и есть их защита
   ([ADR-0015](adr/0015-secure-by-default.md)).
2. **`flush_interval -1` обязателен** для SSE: с буферизацией события управления воспроизведением
   доходили бы пачками ([ADR-0024](adr/0024-playback-ownership-via-sse.md)).
3. **`request_body max_size`** — внешний рубеж лимита загрузки
   ([ADR-0025](adr/0025-upload-limits-in-three-places.md)).

Плюс заголовки безопасности и сжатие; TLS Caddy получает и продлевает сам. Отдельный слушатель на
`:8081` отдаёт только `/health` — для мониторинга без TLS.

## Развёртывание

```bash
git clone <repo> && cd music-streaming
cp .env.example .env
$EDITOR .env
GIT_SHA=$(git rev-parse HEAD) docker compose up -d --build
```

Compose откажется стартовать без `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY`, `OWNER_PASSWORD`,
`GRAFANA_PASSWORD`, `PUBLIC_DOMAIN` — синтаксис `${VAR:?…}`.

Обновление:

```bash
git pull
GIT_SHA=$(git rev-parse HEAD) docker compose up -d --build
```

Миграции применятся сами при старте. **Простой на время перезапуска неизбежен** — схемы с нулевым
простоем нет ([ADR-0027](adr/0027-single-instance-deployment.md)).

## Локальная разработка

```bash
make db        # Postgres в Docker, порт на 127.0.0.1:5432
make backend   # dotnet watch run, http://localhost:5199
make frontend  # next dev, http://localhost:3000
make dev       # всё сразу
make test      # тесты бэкенда (нужен Docker)
make stop
```

`make db` использует `docker-compose.dev.yml` — двухстрочный override, публикующий порт Postgres на
петлю. В боевом файле порт базы не публикуется вовсе.

> **Ловушка: `make db` требует заполненного `.env` целиком.** Compose разбирает **весь** файл, даже
> когда вы поднимаете один сервис, поэтому отсутствие, скажем, `GRAFANA_PASSWORD` роняет команду:
>
> ```
> error while interpolating services.grafana.environment.GF_SECURITY_ADMIN_PASSWORD:
> required variable GRAFANA_PASSWORD is missing a value
> ```
>
> Grafana при этом не запускается и не нужна. Лечится заполнением всех пяти обязательных переменных
> (см. [`11-configuration.md`](11-configuration.md)) — значение может быть любым, если сервис вам
> локально не нужен.
>
> Обходной путь, если правка `.env` нежелательна, — поднять Postgres напрямую, с теми же параметрами,
> что ждёт `appsettings.Development.json`:
>
> ```bash
> docker run -d --name caimack-pg -p 127.0.0.1:5432:5432 \
>     -e POSTGRES_DB=music -e POSTGRES_USER=music -e POSTGRES_PASSWORD=1234 \
>     postgres:17-alpine
> ```

> В `Makefile` в списке `.PHONY` остались цели мобильного приложения (`mobile-*`), самих целей нет —
> приложение удалено. Безвредно, но при случае стоит подчистить.

## Релиз

```bash
make release VERSION=1.2.0
```

## Куда дальше

[`14-conventions.md`](14-conventions.md) — как писать код, который примут.
