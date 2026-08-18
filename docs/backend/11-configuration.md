# 11. Конфигурация

## Обязательные переменные

`docker-compose.yml` использует синтаксис `${VAR:?сообщение}` — без них compose откажется даже запускаться:

| Переменная | Для чего |
|---|---|
| `POSTGRES_PASSWORD` | Пароль базы |
| `JWT_SIGNING_KEY` | Подпись токенов, **32+ байта**: `openssl rand -base64 48` |
| `OWNER_PASSWORD` | Пароль первой учётной записи, минимум 8 символов |
| `GRAFANA_PASSWORD` | Вход в Grafana |
| `PUBLIC_DOMAIN` | Домен для Caddy и обратного адреса Last.fm |


## Полный справочник настроек

### `ConnectionStrings`

| Ключ | Переменная |
|---|---|
| `Default` | `ConnectionStrings__Default` |

В dev берётся из `appsettings.Development.json`
(`Host=localhost;Port=5432;Database=music;Username=music;Password=1234` — под `make db`).

### `Jwt`

| Ключ | Env | По умолчанию | Правило | Последствие изменения |
|---|---|---|---|---|
| `SigningKey` | `JWT_SIGNING_KEY` | — | ≥32 байт, не из списка утёкших | Смена **инвалидирует все выданные токены** — все выходят из системы |
| `AccessTokenMinutes` | `JWT_ACCESS_TOKEN_MINUTES` | 10 (в `appsettings.json` — 30) | > 0 | Дольше — реже обновления, но дольше живёт скомпрометированный токен и позже применяется смена роли |
| `RefreshTokenDays` | `JWT_REFRESH_TOKEN_DAYS` | 30 | > 0 | Как долго можно не вводить пароль |
| `Issuer`, `Audience` | — | `music-streaming` | — | Смена инвалидирует выданные токены |

### `Storage`

| Ключ | Env | По умолчанию | Правило | Комментарий |
|---|---|---|---|---|
| `RootPath` | — (`/storage` в контейнере) | `/storage` | не пусто | Том с хоста, `MUSIC_STORAGE_PATH` |
| `MaxUploadBytes` | `MAX_UPLOAD_BYTES` | 209715200 (200 МиБ) | > 0 | Согласуйте с `MAX_UPLOAD_BODY_BYTES`! |
| `MaxImageUploadBytes` | — | 8388608 (8 МиБ) | > 0 | Обложки |


### `Playback`

| Ключ | Env | По умолчанию | Правило | Комментарий |
|---|---|---|---|---|
| `HistoryThresholdSeconds` | `HISTORY_THRESHOLD_SECONDS` | 30 | > 0 | Сколько нужно прослушать, чтобы трек попал в историю. **Не** порог скроббла Last.fm — у него своё правило |
| `HistoryRetentionEntries` | — | 1000 | > 0 | Сколько записей истории хранится на пользователя |

### `Transcode`

| Ключ | Env | По умолчанию | Правило |
|---|---|---|---|
| `Enabled` | `TRANSCODE_ENABLED` | `true` | — |
| `LowBitrateKbps` | `TRANSCODE_LOW_KBPS` | 64 | 32–320 |
| `NormalBitrateKbps` | `TRANSCODE_NORMAL_KBPS` | 128 | 32–320 |
| `HighBitrateKbps` | `TRANSCODE_HIGH_KBPS` | 192 | 32–320 |
| `FfmpegPath` | — | `ffmpeg` | не пусто |


### `Lastfm`

| Ключ | Env | По умолчанию | Комментарий |
|---|---|---|---|
| `ApiKey` | `LASTFM_API_KEY` | пусто | Без ключа и секрета интеграция не предлагается |
| `ApiSecret` | `LASTFM_API_SECRET` | пусто | |
| `PublicUrl` | — (`https://${PUBLIC_DOMAIN}`) | пусто | Нужен для построения обратного адреса OAuth |

### `Owner`

| Ключ | Env | По умолчанию | Комментарий |
|---|---|---|---|
| `Username` | `OWNER_USERNAME` | `admin` | Приводится к нижнему регистру |
| `Password` | `OWNER_PASSWORD` | — | Минимум 8 символов. Требуется, только если пользователя ещё нет |
| `DisplayName` | `OWNER_DISPLAY_NAME` | = `Username` | |
| `ResetPasswordOnStartup` | `OWNER_RESET_PASSWORD` | `false` | **Осторожно:** при `true` пароль возвращается к значению из `.env` при каждом старте |

### `Recommendations`

**Затухание**

| Ключ | По умолчанию | Смысл |
|---|---|---|
| `TrackHalfLifeDays` | 45 | Интерес к треку убывает вдвое за столько дней |
| `ArtistHalfLifeDays` | 90 | Вкус к исполнителю меняется медленнее |
| `GenreHalfLifeDays` | 90 | То же для жанра |
| `ScoreSoftness` | 3 | Какой накопленный вес считается сильным предпочтением |
| `FreshnessWindowDays` | 30 | Сколько трек считается свежим |

**Зрелость профиля**

| Ключ | По умолчанию | Правило |
|---|---|---|
| `WarmThreshold` | 10 | ≥ 0 |
| `MatureThreshold` | 100 | > `WarmThreshold` |

**Полки**

| Ключ | Env | По умолчанию | Правило |
|---|---|---|---|
| `Enabled` | `RECOMMENDATIONS_ENABLED` | `true` | Выключает **воркеры**; чтение работает и откатывается к холодному старту |
| `ShelfSize` | `RECOMMENDATIONS_SHELF_SIZE` | 12 | > 0 |
| `CandidateLimit` | — | 600 | ≥ `ShelfSize` |
| `PerSourceLimit` | — | 120 | Потолок одного источника кандидатов |
| `SimilarTopK` | — | 50 | Сколько соседей хранится на трек |
| `ExplorationRatio` | `RECOMMENDATIONS_EXPLORATION_RATIO` | 0.25 | [0, 1] |
| `DiscoveryExplorationRatio` | — | 0.60 | [0, 1] |
| `DiversityLambda` | — | 0.30 | [0, 1) |
| `MaxPerArtist` / `MaxPerAlbum` / `MaxPerGenre` | — | 2 / 2 / 4 | > 0 |

**Штрафы** — `JustPlayedPenalty` 0.15, `RecentlyPlayedPenalty` 0.60,
`UnclickedImpressionPenalty` 0.50, `DislikedTrackPenalty` 0.10, `DislikedArtistPenalty` 0.30; окна —
`JustPlayedHours` 24, `RecentlyPlayedDays` 7, `ImpressionCooldownDays` 7.

**Похожесть и коллаборативная фильтрация** — `CollaborativeShrinkage` 5,
`CollaborativeBlendPivot` 10, `UserCfMinUsers` 5, `UserCfMinInteractions` 30. Последние два выключают
межпользовательские рекомендации, пока слушателей не станет достаточно, чтобы статистика хоть что-то
значила.

**Расписание и хранение**

| Ключ | Env | По умолчанию | Правило |
|---|---|---|---|
| `CacheTtlHours` | — | 6 | > 0 |
| `RegenerationDebounceSeconds` | — | 60 | |
| `SimilarityIntervalHours` | — | 6 | |
| `StartupDelaySeconds` | — | 30 | У `LibraryMaintenanceWorker` удваивается |
| `EventRetentionDays` | `RECOMMENDATIONS_EVENT_RETENTION_DAYS` | 180 | > 0 |
| `ImpressionRetentionDays` | — | 60 | |
| `MaxEventsPerRequest` | — | 100 | > 0 |

### `Security`

| Ключ | По умолчанию | Комментарий |
|---|---|---|
| `LoginAttemptsPerMinute` | 10 | Попыток входа с одного IP. Поднимают, когда за одним NAT живёт несколько человек |

Читается напрямую `configuration.GetValue`, без класса настроек.

### `ForwardedHeaders`

| Ключ | По умолчанию | Комментарий |
|---|---|---|
| `KnownNetworks` | `127.0.0.0/8`, `::1/128`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` | Кому доверять заголовки прокси |

`ForwardLimit = 1` зашит в коде: доверяется ровно один переход.

### `Cors`

| Ключ | По умолчанию | Комментарий |
|---|---|---|
| `AllowedOrigins` | `["http://localhost:3000"]` | Применяется **только** в Development |


## Настройки, которые задаются не приложением

| Что | Где | Комментарий |
|---|---|---|
| `MAX_UPLOAD_BODY_BYTES` | Caddy | Должно быть **выше** `MAX_UPLOAD_BYTES` |
| `PUBLIC_DOMAIN`, `HTTP_PORT`, `HTTPS_PORT` | Caddy | |
| `BACKEND_PORT`, `GRAFANA_PORT` | compose | Публикуются **только на `127.0.0.1`** |
| `PUID`, `PGID` | compose | От кого работает контейнер; том хранилища должен принадлежать им |
| `PROMETHEUS_RETENTION`, `LOKI_RETENTION` | compose | |
| `AUDIODB_API_KEY`, `AUDIODB_REQUEST_DELAY_MS` | утилита изображений | |
| `GIT_SHA` | аргумент сборки | `.git` в контекст образа не попадает |

## Локальная разработка

`appsettings.Development.json` переопределяет ровно три вещи: путь хранилища (`../../../storage`),
строку подключения к локальному Postgres и уровень логирования SQL.

Секреты для локальной разработки — user secrets, а не файл в репозитории:

```bash
cd backend/src/MusicStreaming.Api
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"
dotnet user-secrets set "Owner:Password" "какой-нибудь-пароль"
```

## Куда дальше

[`12-testing.md`](12-testing.md) — как проверять, что изменения ничего не сломали.
