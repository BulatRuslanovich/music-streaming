# Бэкенд Caimack — документация


### Этап 1. Запустить

Читать: [`01-overview.md`](01-overview.md) → [`11-configuration.md`](11-configuration.md) →
[`13-operations.md`](13-operations.md).

Сделать руками:

```bash
cp .env.example .env && $EDITOR .env
make db 
cd backend/src/MusicStreaming.Api && dotnet run
```

### Этап 2. Понять форму

Читать: [`02-architecture.md`](02-architecture.md) → [`03-request-lifecycle.md`](03-request-lifecycle.md)
→ [`04-domain-model.md`](04-domain-model.md).


### Этап 3. Подсистемы

Читать: [`05-persistence.md`](05-persistence.md) → [`06-security.md`](06-security.md) →
[`07-media-pipeline.md`](07-media-pipeline.md) → [`08-recommendations.md`](08-recommendations.md) →
[`09-integrations.md`](09-integrations.md) → [`10-observability.md`](10-observability.md).

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

