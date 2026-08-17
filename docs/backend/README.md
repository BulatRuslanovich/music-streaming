# Бэкенд Caimack — документация

Это точка входа. Если вы только что склонировали репозиторий и вам предстоит развивать бэкенд — читайте
отсюда и по порядку, описанному ниже.

Документация отвечает на два разных вопроса, и они разнесены по разным файлам:

- **как устроено** — пронумерованные главы `01`–`14` в этом каталоге;
- **почему устроено именно так** — записи в [`adr/`](adr/), по одной на решение.

Главы описывают текущее состояние. ADR фиксируют выбор: какие были варианты, что выбрали, чем за это
платим и при каких условиях решение нужно пересматривать. Когда в главе встречается «почему не X» —
там будет ссылка на ADR, а не пересказ.

---

## Маршрут на первый день

Порядок подобран так, чтобы каждый шаг опирался только на уже прочитанное. Не перескакивайте: глава
про рекомендации стоит предпоследней не потому, что она наименее важная, а потому что она опирается
на модель данных, хранилище, фоновые процессы и метрики сразу.

### Этап 1. Запустить (≈1 час)

Читать: [`01-overview.md`](01-overview.md) → [`11-configuration.md`](11-configuration.md) →
[`13-operations.md`](13-operations.md).

Сделать руками:

```bash
cp .env.example .env && $EDITOR .env     # заполнить ВСЕ обязательные переменные
make db                                   # Postgres в Docker
cd backend/src/MusicStreaming.Api && dotnet run
```

> Обязательных переменных пять, и заполнить нужно все — даже `GRAFANA_PASSWORD`, хотя Grafana
> локально не поднимается. Compose разбирает весь файл целиком, и `make db` без неё не запустится.
> Подробности и обходной путь — в [`13-operations.md`](13-operations.md).

Затем открыть `http://localhost:5199/docs`, залогиниться владельцем (`Owner:Username` /
`Owner:Password` из настроек), загрузить один mp3 через `POST /api/tracks/upload` и получить его
обратно через `GET /api/tracks/{id}/stream`.

**Контрольные вопросы этапа:**
- Откуда приложение взяло строку подключения и почему оно не упало, хотя `.env` в `backend/` не читается?
- Почему учётная запись владельца существует, хотя её никто не создавал?
- Почему `/docs` и `/metrics` не начинаются с `/api`?

### Этап 2. Понять форму (≈2 часа)

Читать: [`02-architecture.md`](02-architecture.md) → [`03-request-lifecycle.md`](03-request-lifecycle.md)
→ [`04-domain-model.md`](04-domain-model.md).

Сделать руками: пройти один запрос целиком по коду, ничего не пропуская. Рекомендуемый маршрут —
`GET /api/albums/{id}`, он короткий и задевает все слои:

`Api/Controllers/AlbumsController.cs:22` → `Application/Services/CatalogService.cs` →
`Application/Common/Projections.cs` → `IApplicationDbContext` → `Infrastructure/Persistence/ApplicationDbContext.cs`
→ SQL.

Потом повторить то же с `POST /api/tracks/upload` — это самый длинный путь в приложении.

**Контрольные вопросы этапа:**
- Почему `MusicStreaming.Api` не ссылается на `MusicStreaming.Application` напрямую, но вызывает `AddApplication()`?
- Что случится с ответом, если сервис бросит `NotFoundException`, и кто это превращает в 404?
- Почему `ExceptionHandlingMiddleware` стоит в конвейере раньше логирования запросов, а не позже?

### Этап 3. Подсистемы (≈3 часа)

Читать: [`05-persistence.md`](05-persistence.md) → [`06-security.md`](06-security.md) →
[`07-media-pipeline.md`](07-media-pipeline.md) → [`08-recommendations.md`](08-recommendations.md) →
[`09-integrations.md`](09-integrations.md) → [`10-observability.md`](10-observability.md).

Сделать руками: послушать несколько треков в веб-интерфейсе, затем посмотреть, что появилось в
таблицах `playback_events`, `user_track_affinities`, `user_taste_profiles` и `recommendation_cache`.
Это единственный способ прочувствовать конвейер рекомендаций.

**Контрольные вопросы этапа:**
- Почему у событий воспроизведения ровно один писатель и что сломается, если их станет два?
- Что произойдёт с активными сессиями, если администратор деактивирует пользователя? А если поменяет ему роль?
- Почему при первом запросе трека в качестве `High` слушатель всё равно сразу получает звук?

### Этап 4. Начать менять (≈2 часа)

Читать: [`12-testing.md`](12-testing.md) → [`14-conventions.md`](14-conventions.md).

Сделать руками: пройти сквозной сценарий из раздела ниже и отправить получившееся в PR.

---

## Карта документов

| Документ | На какой вопрос отвечает |
|---|---|
| [`01-overview.md`](01-overview.md) | Что это за продукт, где его границы, из чего он собран |
| [`02-architecture.md`](02-architecture.md) | Какие есть слои, что кому можно знать, где что лежит |
| [`03-request-lifecycle.md`](03-request-lifecycle.md) | Что происходит с HTTP-запросом от сокета до ответа |
| [`04-domain-model.md`](04-domain-model.md) | Какие есть сущности и как они связаны |
| [`05-persistence.md`](05-persistence.md) | Как модель ложится в Postgres, как писать запросы и миграции |
| [`06-security.md`](06-security.md) | Как работают вход, токены, роли и ограничения частоты |
| [`07-media-pipeline.md`](07-media-pipeline.md) | Путь файла: загрузка → хранилище → транскод → отдача |
| [`08-recommendations.md`](08-recommendations.md) | Как из прослушиваний получаются полки на главной |
| [`09-integrations.md`](09-integrations.md) | Last.fm и общий механизм исходящих доставок |
| [`10-observability.md`](10-observability.md) | Логи, метрики, дашборды, healthcheck |
| [`11-configuration.md`](11-configuration.md) | Все настройки: где заданы, что означают, что ломают |
| [`12-testing.md`](12-testing.md) | Как устроены тесты и как писать новые |
| [`13-operations.md`](13-operations.md) | Docker, compose, релизы, бэкапы, эксплуатация |
| [`14-conventions.md`](14-conventions.md) | Стиль кода, комментариев, требования CI, чек-лист PR |
| [`adr/`](adr/) | Обоснование каждого нетривиального решения |

---

## Сквозной пример: добавить поле в трек

Самый быстрый способ увидеть, как связаны слои, — провести одно поле через все. Допустим, нужно
хранить у трека номер диска (`DiscNumber`).

| # | Что делаем | Где |
|---|---|---|
| 1 | Добавить свойство в сущность | `backend/src/MusicStreaming.Domain/Entities/Track.cs` |
| 2 | При необходимости описать колонку (тип, индекс, ограничения) | `backend/src/MusicStreaming.Infrastructure/Persistence/Configurations/LibraryConfiguration.cs` |
| 3 | Создать миграцию | `dotnet ef migrations add AddTrackDiscNumber` — см. [`05-persistence.md`](05-persistence.md) |
| 4 | Добавить поле в DTO | `backend/src/MusicStreaming.Application/Dtos/LibraryDtos.cs` |
| 5 | Протянуть в проекцию | `backend/src/MusicStreaming.Application/Common/Projections.cs` |
| 6 | Заполнить при загрузке из тегов | `backend/src/MusicStreaming.Application/Services/TrackUploadService.cs`, `Abstractions/IAudioMetadataReader.cs`, `Infrastructure/Metadata/TagLibAudioMetadataReader.cs` |
| 7 | Разрешить редактирование | `backend/src/MusicStreaming.Application/Services/TrackEditService.cs` |
| 8 | Покрыть тестом | `backend/tests/MusicStreaming.IntegrationTests/` — см. [`12-testing.md`](12-testing.md) |
| 9 | Проверить схему | `dotnet run`, открыть `/docs`, убедиться, что поле видно в `TrackDto` |

Обратите внимание, чего в списке **нет**: не нужно править репозиторий (его нет), маппер (его нет),
регистрацию в DI (сервисы уже зарегистрированы), контроллер (он ничего не знает о полях). Это прямое
следствие решений [ADR-0003](adr/0003-dbcontext-instead-of-repositories.md) и
[ADR-0007](adr/0007-manual-projections-instead-of-automapper.md).

---

## «Почему не …?» — указатель на ADR

Вопросы, которые возникают у любого, кто впервые открывает этот код:

| Вопрос | Ответ |
|---|---|
| Почему слои, а не вертикальные срезы? | [ADR-0001](adr/0001-layered-clean-architecture.md) |
| Почему нет MediatR и команд/запросов? | [ADR-0002](adr/0002-no-mediatr.md) |
| Почему нет репозиториев и Unit of Work? | [ADR-0003](adr/0003-dbcontext-instead-of-repositories.md) |
| Почему EF Core торчит в слое Application? | [ADR-0004](adr/0004-efcore-in-application-layer.md) |
| Почему исключения, а не `Result<T>`? | [ADR-0005](adr/0005-exceptions-instead-of-result.md) |
| Почему нет FluentValidation? | [ADR-0006](adr/0006-imperative-validation.md) |
| Почему нет AutoMapper? | [ADR-0007](adr/0007-manual-projections-instead-of-automapper.md) |
| Почему идентификаторы — UUIDv7, а не int? | [ADR-0008](adr/0008-uuid-v7-identifiers.md) |
| Почему часть данных лежит в jsonb? | [ADR-0009](adr/0009-jsonb-for-value-objects.md) |
| Почему ранжирование поиска написано на SQL? | [ADR-0010](adr/0010-search-rank-in-sql.md) |
| Почему миграции применяются при старте приложения? | [ADR-0011](adr/0011-migrate-on-startup.md) |
| Почему владелец восстанавливается в правах на каждом старте? | [ADR-0012](adr/0012-owner-reseeded-on-startup.md) |
| Почему токен лежит в cookie, а не только в заголовке? | [ADR-0013](adr/0013-jwt-in-cookie.md) |
| Почему ротация refresh-токенов так усложнена? | [ADR-0014](adr/0014-refresh-token-rotation.md) |
| Почему все ручки закрыты по умолчанию? | [ADR-0015](adr/0015-secure-by-default.md) |
| Почему пользователи не удаляются физически? | [ADR-0016](adr/0016-soft-delete-users.md) |
| Почему файлы на диске, а не в S3/MinIO? | [ADR-0017](adr/0017-filesystem-instead-of-s3.md) |
| Почему транскод ленивый и отдаёт оригинал? | [ADR-0018](adr/0018-lazy-transcoding.md) |
| Почему три разные таблицы про прослушивания? | [ADR-0019](adr/0019-three-listening-stores.md) |
| Почему у affinity инкрементальное затухание? | [ADR-0020](adr/0020-incremental-decay.md) |
| Почему кэш рекомендаций хранит только id? | [ADR-0021](adr/0021-cache-stores-ids-only.md) |
| Почему события воспроизведения можно терять? | [ADR-0022](adr/0022-droppable-telemetry.md) |
| Почему скробблинг идёт через outbox? | [ADR-0023](adr/0023-generic-outbox.md) |
| Почему эксклюзивность воспроизведения держит SSE-соединение? | [ADR-0024](adr/0024-playback-ownership-via-sse.md) |
| Почему лимит загрузки задан в трёх местах? | [ADR-0025](adr/0025-upload-limits-in-three-places.md) |
| Почему в тестах нет моков? | [ADR-0026](adr/0026-no-mocks-real-postgres.md) |
| Почему нельзя запустить два экземпляра бэкенда? | [ADR-0027](adr/0027-single-instance-deployment.md) |

---

## Если что-то не сходится

Документация описывает состояние кода на момент написания. Если глава расходится с кодом — **прав
код**, а расхождение стоит починить в том же PR, что и изменение. Правило простое: меняешь поведение,
описанное в главе, — правишь главу; меняешь решение, зафиксированное в ADR, — не переписываешь ADR, а
добавляешь новый со ссылкой «заменяет NNNN».
