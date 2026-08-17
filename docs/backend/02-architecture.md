# 02. Архитектура

## Четыре слоя

Бэкенд построен по слоистой схеме (Clean Architecture / «луковица»). Зависимости идут строго в одну
сторону — внутрь:

```mermaid
flowchart RL
    Api["<b>Api</b><br/>контроллеры, конвейер, композиция"]
    Inf["<b>Infrastructure</b><br/>EF Core, диск, ffmpeg, HTTP, фоновые процессы"]
    App["<b>Application</b><br/>сценарии, DTO, порты, настройки"]
    Dom["<b>Domain</b><br/>сущности, перечисления, чистые правила"]

    Api --> Inf --> App --> Dom
```

Ссылки между проектами буквально такие: `Api → Infrastructure → Application → Domain`. Каждый слой
знает только о том, что внутри него.

**Ловушка, на которой спотыкаются все.** `MusicStreaming.Api` **не ссылается** на
`MusicStreaming.Application` напрямую — только транзитивно, через `Infrastructure`. При этом
[`Program.cs:16`](../../backend/src/MusicStreaming.Api/Program.cs#L16) вызывает `AddApplication()`, а
контроллеры инжектят сервисы приложения. Это работает благодаря транзитивной передаче ссылок в
современных SDK-проектах. Если вы когда-нибудь захотите добавить `<PrivateAssets>` к ссылке
`Infrastructure`, сборка `Api` развалится в десятках мест.

## Что лежит в каждом слое

### Domain — `backend/src/MusicStreaming.Domain/`

Сущности, перечисления и несколько чистых функций. **Ни одного NuGet-пакета** — это проверяемая
граница: если кто-то попытается притащить сюда EF Core или ASP.NET, это сразу видно в `.csproj`.

```
Entities/                 Artist, Album, Track, User, Playlist, Favorite, …
Entities/Recommendations/ PlaybackEvent, Affinity, UserTasteProfile, TrackStats, …
Entities/Integrations/    LastfmAccount, OutboundJob
Common/                   Normalize, ArtistNames, AudioQuality
```

Сущности здесь **анемичные**: свойства и почти никакого поведения. Нет базового класса сущности, нет
агрегатов, нет доменных событий. Логика живёт в сервисах слоя Application. Это осознанный выбор для
проекта такого размера — см. [ADR-0001](adr/0001-layered-clean-architecture.md).

Исключение — чистые функции в `Common/`. `Normalize.Key()` приводит строку к ключу сравнения
(нижний регистр, схлопнутые пробелы), `ArtistNames.Split()` разбирает «A feat. B / C» на список
исполнителей. Они в Domain, потому что от них зависит смысл нормализованных колонок в базе, и они
обязаны вести себя одинаково при загрузке, поиске и сравнении.

### Application — `backend/src/MusicStreaming.Application/`

Самый содержательный слой. Здесь живут сценарии использования.

```
Abstractions/       порты во внешний мир (см. таблицу ниже)
Services/           ~25 сервисов, по одному на предметную область
Services/Recommendations/  конвейер рекомендаций
Services/Integrations/     Last.fm и очередь исходящих
Recommendations/    типы движка рекомендаций
Recommendations/Scoring/   чистая математика скоринга, покрыта юнит-тестами
Dtos/               контракты HTTP-ответов
Options/            классы настроек
Common/             общие хелперы: проекции, пагинация, исключения, валидация значений
```

Сервис — обычный класс, зарегистрированный как `Scoped` в
[`Application/DependencyInjection.cs`](../../backend/src/MusicStreaming.Application/DependencyInjection.cs).
Никаких команд, запросов, шины сообщений и пайплайна поведений: контроллер вызывает метод сервиса
напрямую — см. [ADR-0002](adr/0002-no-mediatr.md).

### Infrastructure — `backend/src/MusicStreaming.Infrastructure/`

Реализации портов, по папке на технологию, плюс все фоновые процессы.

```
Persistence/      ApplicationDbContext, Configurations/, Migrations/, DatabaseInitializer
Storage/          FileSystemMusicStorage
Audio/            FfmpegAudioTranscoder, TranscodeWorker
Imaging/          ImageSharpImageProcessor, CoverBackfillService
Metadata/         TagLibAudioMetadataReader
Security/         BCryptPasswordHasher, JwtTokenService, DataProtectionSecretProtector
Integrations/     LastfmClient, OutboundJobWorker, OutboundRetry
Recommendations/  EventIngestWorker, RecommendationWorker, LibraryMaintenanceWorker, SimilarityMaintenance
```

### Api — `backend/src/MusicStreaming.Api/`

Самый тонкий слой: 19 контроллеров, одно middleware и шесть файлов настройки хоста в `Startup/`.
Типичное действие контроллера — одна строка:

```csharp
[HttpGet("{id:guid}")]
public async Task<ActionResult<AlbumDetailDto>> Get(Guid id, CancellationToken ct) =>
    Ok(await catalog.GetAlbumAsync(id, ct));
```

Если в контроллере появляются `if`, обращения к базе или сборка ответа из нескольких кусков — это
сигнал, что логика утекла не туда.

## Порты и адаптеры

Всё, что уводит за пределы процесса, объявлено интерфейсом в
[`Application/Abstractions/`](../../backend/src/MusicStreaming.Application/Abstractions/) и
реализовано в Infrastructure. Полный список — здесь заканчивается вся «внешность» приложения:

| Порт | Адаптер | Что за ним |
|---|---|---|
| `IApplicationDbContext` | `Persistence/ApplicationDbContext` | PostgreSQL через EF Core |
| `IMusicStorage` | `Storage/FileSystemMusicStorage` | Файлы на диске |
| `IAudioMetadataReader` | `Metadata/TagLibAudioMetadataReader` | Чтение тегов (TagLibSharp) |
| `IAudioTranscoder` | `Audio/FfmpegAudioTranscoder` | Внешний процесс ffmpeg |
| `IImageProcessor` | `Imaging/ImageSharpImageProcessor` | Ресайз обложек (ImageSharp) |
| `IPasswordHasher` | `Security/BCryptPasswordHasher` | BCrypt |
| `ITokenService` | `Security/JwtTokenService` | Выпуск JWT и refresh-токенов |
| `ISecretProtector` | `Security/DataProtectionSecretProtector` | Шифрование чужих секретов (ASP.NET Data Protection) |
| `ILastfmApi` | `Integrations/LastfmClient` | HTTP к Last.fm |
| `ICurrentUser` | `Api`: `ClaimsPrincipalCurrentUser` | Кто выполняет запрос |

`ICurrentUser` — единственный порт, чей адаптер живёт не в Infrastructure, а в `Api`
([`Program.cs:19-20`](../../backend/src/MusicStreaming.Api/Program.cs#L19-L20)): личность
берётся из `ClaimsPrincipal`, а это понятие веб-слоя. Благодаря этому порту сервисы приложения не
знают ни про `HttpContext`, ни про claims — им доступен только `Id` и `IsAuthenticated`.

## Осознанные отступления от канона

Слоистая архитектура в чистом виде здесь не соблюдается, и это сделано намеренно. Ниже — все места,
где вы заметите расхождение с учебником, чтобы не пытаться «починить» их в первый же день.

### 1. EF Core виден в слое Application

`IApplicationDbContext` возвращает `DbSet<T>`, а также отдаёт наружу `Database`, `Set<TEntity>()` и
`ChangeTracker`. Формально это протечка инфраструктуры внутрь. Практически — цена альтернативы выше:
статистике нужен `AT TIME ZONE`, поиску нужна SQL-функция ранжирования в `ORDER BY`, загрузке нужно
уметь откатывать незакоммиченные сущности.

Каждая из трёх escape-hatch снабжена комментарием прямо в интерфейсе
([`IApplicationDbContext.cs:41-56`](../../backend/src/MusicStreaming.Application/Abstractions/IApplicationDbContext.cs#L41-L56)).
Обоснование целиком — [ADR-0004](adr/0004-efcore-in-application-layer.md).

### 2. Нет репозиториев и Unit of Work

Их роль исполняют сам `IApplicationDbContext` (репозиторий) и `SaveChangesAsync` (единица работы).
Сервис пишет LINQ-запрос напрямую. См. [ADR-0003](adr/0003-dbcontext-instead-of-repositories.md).

### 3. Нет маппера

Преобразование сущность → DTO собрано в
[`Application/Common/Projections.cs`](../../backend/src/MusicStreaming.Application/Common/Projections.cs)
как `Expression<Func<Track, TrackDto>>`. Выражение подставляется в LINQ, поэтому проекция уезжает в
SQL и из базы приезжают только нужные колонки. AutoMapper так не умеет без дополнительных усилий.
См. [ADR-0007](adr/0007-manual-projections-instead-of-automapper.md).

### 4. Валидация — императивная, внутри сервисов

Нет FluentValidation и атрибутов на DTO. Сервис проверяет входные данные сам и бросает
`ValidationException`. Правила уровня значения вынесены в маленькие статические классы:
`PasswordPolicy`, `AudioUpload`, `ImageUpload`, `LyricsText`, `SearchTerm`, `DownloadFileName`.
См. [ADR-0006](adr/0006-imperative-validation.md).

Единственное исключение — **настройки**: они валидируются декларативно и на старте, см.
[`11-configuration.md`](11-configuration.md).

### 5. Ошибки — исключения, а не `Result<T>`

Иерархия `AppException` в
[`Application/Common/AppExceptions.cs`](../../backend/src/MusicStreaming.Application/Common/AppExceptions.cs)
несёт в себе HTTP-код, а middleware превращает её в ответ. См.
[ADR-0005](adr/0005-exceptions-instead-of-result.md) и [`03-request-lifecycle.md`](03-request-lifecycle.md).

## Фоновые процессы

Шесть `BackgroundService`, все зарегистрированы в
[`Infrastructure/DependencyInjection.cs:126-131`](../../backend/src/MusicStreaming.Infrastructure/DependencyInjection.cs#L126-L131):

| Процесс | Чем занят | Подробности |
|---|---|---|
| `EventIngestWorker` | Единственный писатель событий воспроизведения | [`08`](08-recommendations.md) |
| `RecommendationWorker` | Пересчёт профиля вкуса и полок | [`08`](08-recommendations.md) |
| `LibraryMaintenanceWorker` | Популярность, похожесть, чистка старых событий | [`08`](08-recommendations.md) |
| `TranscodeWorker` | Перекодирование в Opus по очереди | [`07`](07-media-pipeline.md) |
| `OutboundJobWorker` | Доставка исходящих задач (скробблинг) | [`09`](09-integrations.md) |
| `CoverBackfillService` | Разовая переупаковка старых обложек в webp | [`07`](07-media-pipeline.md) |

Общаются они с веб-частью через синглтон-очереди, объявленные в слое Application: `TranscodeQueue`,
`EventIngestQueue`, `RecommendationRefreshQueue`, `PlaybackSessionRegistry`. Очереди — **в памяти
процесса**, и это ключевое ограничение всей системы: см.
[ADR-0027](adr/0027-single-instance-deployment.md).

## Правила, которые стоит держать в голове

1. **Зависимость только внутрь.** Если в Application понадобился класс из Infrastructure — нужен
   новый порт в `Abstractions/`, а не ссылка.
2. **Контроллер не думает.** Он разбирает HTTP и зовёт сервис. Всё остальное — вниз.
3. **Domain ничего не знает.** Новый пакет в `MusicStreaming.Domain.csproj` — почти наверняка ошибка.
4. **Сервис не знает про HTTP.** Ни `HttpContext`, ни статус-кодов, ни заголовков. Только
   `ICurrentUser` и исключения из `AppExceptions`.
5. **Новая внешняя система = порт + адаптер.** Даже если реализация в три строки.

## Куда дальше

[`03-request-lifecycle.md`](03-request-lifecycle.md) — что происходит с запросом, когда он приходит.
