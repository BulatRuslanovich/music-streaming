# 13. Сборка и эксплуатация

## Образ

[`backend/Dockerfile`](../../backend/Dockerfile) — три стадии.

### `build` — `sdk:10.0-alpine`

Сначала копируются **только** `.csproj`, `Directory.Build.props` и `.slnx`, затем идёт `restore`, и
лишь потом копируется `src/`. Это классический приём кэширования слоёв: правка кода не приводит к
повторному восстановлению пакетов.

```dockerfile
ARG GIT_SHA=""
RUN dotnet publish src/MusicStreaming.Api/MusicStreaming.Api.csproj \
    -c Release -o /app --no-restore -p:SourceRevisionId=$GIT_SHA
```

`GIT_SHA` передаётся снаружи, потому что `.git` в контекст сборки не попадает. Без него SDK не
допишет `+<sha>` к `InformationalVersion`, и интерфейс покажет версию без коммита
([`10-observability.md`](10-observability.md)).

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
$EDITOR .env                       # заполнить 5 обязательных переменных
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

[`scripts/release.sh`](../../scripts/release.sh) правит версию **в двух местах сразу** —
`backend/Directory.Build.props` и `frontend/package.json`. Они не должны разъезжаться: бэкенд отдаёт
свою версию в `/api/system`, и подвал интерфейса показывает её рядом с версией фронта именно затем,
чтобы рассинхрон было видно.

Скрипт отказывается работать при незакоммиченных изменениях, при существующем теге и при версии, не
похожей на `X.Y.Z[-suffix]`. После замены он **проверяет, что `sed` действительно сработал**: молча
не сработавший `sed` оставил бы тег на старой версии.

**Не пушит.** Отправка тега — это публикация, и решать, когда она случится, должен человек. Команды
для отправки и отката печатаются в конце.

## CI

[`.github/workflows/backend-ci.yml`](../../.github/workflows/backend-ci.yml) — на push и PR в
`master`, только при изменениях в `backend/**`. Три параллельных задания:

| Задание | Команда |
|---|---|
| `build` | `dotnet build MusicStreaming.slnx --no-restore -c Release` |
| `test` | `dotnet test MusicStreaming.slnx --no-restore -c Release` |
| `format` | `dotnet format whitespace` и `dotnet format style`, оба `--verify-no-changes` |

На раннерах GitHub есть Docker, поэтому Testcontainers работает и интеграционные тесты **не**
пропускаются.

**Задания на развёртывание нет.** Образы никуда не публикуются, релиз собирается на сервере из
исходников.

Прогоняйте `dotnet format` локально перед пушем — это самая частая причина красного CI.

## Бэкап

Два обязательных элемента:

**1. База.**

```bash
docker compose exec -T postgres pg_dump -U music music | gzip > backup-$(date +%F).sql.gz
```

**2. Каталог хранилища** (`MUSIC_STORAGE_PATH`, по умолчанию `./storage`) — обычный `rsync`. Это
преимущество файловой системы ([ADR-0017](adr/0017-filesystem-instead-of-s3.md)).

> **Не забудьте `.dataprotection`.** Скрытый каталог внутри хранилища содержит ключи шифрования.
> Потеряете — все привязки Last.fm перестанут работать. `rsync` без `-a` или с исключением скрытых
> файлов его пропустит.

Восстановление: развернуть каталог, залить дамп, поднять compose. Приложение само прогонит миграции.

Не требуют бэкапа: `prometheus-data`, `loki-data` (телеметрия), `caddy-data` (сертификаты
перевыпустятся), кэш `transcodes/` внутри хранилища (пересоберётся сам, хотя проще скопировать).

## Эксплуатация: что делать, если

**Приложение не стартует.** Смотрите первые строки `docker compose logs backend`. Валидация настроек
работает на старте и печатает внятное сообщение с именем переменной
([`11-configuration.md`](11-configuration.md)).

**«Database not ready».** Это предупреждение цикла повторов, до 12 попыток. Если после них падает —
проблема в самой базе или в строке подключения.

**Администраторы потеряли доступ.** Проверьте `OWNER_USERNAME` в `.env` и перезапустите бэкенд —
права владельца восстановятся ([ADR-0012](adr/0012-owner-reseeded-on-startup.md)). Если забыт и
пароль — временно поставьте `OWNER_RESET_PASSWORD=true`, перезапустите, **верните `false`** и
перезапустите снова.

**Загрузка падает на большом файле.** Проверьте оба лимита: `MAX_UPLOAD_BYTES` и
`MAX_UPLOAD_BODY_BYTES`, второй должен быть **выше**
([ADR-0025](adr/0025-upload-limits-in-three-places.md)).

**Нет качества выше `Original`.** ffmpeg не найден. Проверьте
`docker compose exec backend ffmpeg -version` и `Transcode:Enabled`.

**Ошибки прав на хранилище.** `PUID`/`PGID` в `.env` должны совпадать с владельцем каталога;
`storage-init` выставляет их при старте.

**Диск заполняется.** Растёт `transcodes/` — кэш перекодирования сам не чистится, его можно удалить
целиком, он пересоберётся. Ещё смотрите ретеншн `playback_events`
(`RECOMMENDATIONS_EVENT_RETENTION_DAYS`).

**Место занимают логи и метрики.** `LOKI_RETENTION` (720 ч) и `PROMETHEUS_RETENTION` (30 дней).

## Куда дальше

[`14-conventions.md`](14-conventions.md) — как писать код, который примут.
