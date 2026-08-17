# 05. Хранение данных

## Что где лежит

| Что | Где |
|---|---|
| Контекст | [`Persistence/ApplicationDbContext.cs`](../../backend/src/MusicStreaming.Infrastructure/Persistence/ApplicationDbContext.cs) |
| Конфигурации сущностей | [`Persistence/Configurations/`](../../backend/src/MusicStreaming.Infrastructure/Persistence/Configurations/) — 6 файлов |
| Миграции | [`Persistence/Migrations/`](../../backend/src/MusicStreaming.Infrastructure/Persistence/Migrations/) — 2 миграции + снимок модели |
| Инициализация и seed | [`Persistence/DatabaseInitializer.cs`](../../backend/src/MusicStreaming.Infrastructure/Persistence/DatabaseInitializer.cs) |
| Регистрация | [`Infrastructure/DependencyInjection.cs:96-108`](../../backend/src/MusicStreaming.Infrastructure/DependencyInjection.cs#L96-L108) |

Провайдер — `Npgsql.EntityFrameworkCore.PostgreSQL` с `EFCore.NamingConventions`:

```csharp
services.AddDbContext<ApplicationDbContext>(options => options
    .UseNpgsql(connectionString, npgsql => npgsql
        .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
    .UseSnakeCaseNamingConvention());
```

`UseSnakeCaseNamingConvention()` превращает `NormalizedTitle` в `normalized_title` автоматически.
Имена таблиц при этом заданы явно (`builder.ToTable("artists")`) — соглашение не должно быть
единственным, что удерживает имя таблицы.

Строка подключения берётся из `ConnectionStrings:Default` и **обязательна**: без неё приложение
бросает исключение на старте, а не при первом запросе.

## Две глобальные конвенции

### Все строки — не длиннее 512

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.Properties<string>().HaveMaxLength(512);
    base.ConfigureConventions(configurationBuilder);
}
```

Это защита от `text` по умолчанию везде, где длина не задумана. Всё, чему 512 мало, **явно**
отказывается от конвенции:

| Поле | Тип | Почему |
|---|---|---|
| `track_lyrics.plain` | `text`, `MaxLength = LyricsText.MaxLength` | Текст песни — килобайты. Предел тот же, что применяет `LyricsText` при разборе тега |
| `lastfm_accounts.session_key` | 2000 | Шифротекст заметно длиннее исходного ключа |
| `recommendation_runs.error` | 2000 | Сообщение исключения |

### Функция поиска

```csharp
modelBuilder
    .HasDbFunction(typeof(SearchRank).GetMethod(nameof(SearchRank.Of))!)
    .HasName(SearchRank.FunctionName);
```

Связывает C#-заглушку с функцией `search_rank` в базе. См. [ADR-0010](adr/0010-search-rank-in-sql.md).

## Типы без ключа

Пять типов — `DailyActivityRow`, `HourlyActivityRow`, `LibraryStatsRow`, `DiagnosticsTotalsRow`,
`ShelfSizeRow` — объявлены `HasNoKey()` и `ExcludeFromMigrations()`.

Это **не таблицы**, а формы ответа сырых запросов. Исключение из миграций обязательно: иначе EF завёл
бы под них настоящие таблицы, в которые никто ничего не пишет.

Нужны они там, где ответ по-настоящему даёт SQL: статистика разворачивает часы в местные сутки через
`AT TIME ZONE`, а сводные счётчики главной сводят десяток агрегатов в один проход.

## jsonb

Общая обвязка — `internal static class JsonColumn` в
[`RecommendationConfiguration.cs`](../../backend/src/MusicStreaming.Infrastructure/Persistence/Configurations/RecommendationConfiguration.cs).
Пара «конвертер + компаратор»:

```csharp
builder.Property(l => l.Synced)
    .HasColumnType("jsonb")
    .HasConversion(JsonColumn.Converter<LyricLine>(), JsonColumn.Comparer<LyricLine>());
```

**Компаратор обязателен.** Без него EF не умеет определить, изменилась ли коллекция, и либо шлёт
`UPDATE` всегда, либо не шлёт никогда. Сравнение идёт через `SequenceEqual`, что корректно **только
потому, что хранимые типы — `record`**. Замена `record` на `class` сломает сохранение молча, без
ошибки. Подробнее — [ADR-0009](adr/0009-jsonb-for-value-objects.md).

## Индексы: что и зачем

Индексы стоят не «на всякий случай» — у каждого есть запрос, который его использует. Наиболее
показательные:

| Индекс | Обслуживает |
|---|---|
| `artists.normalized_name` UNIQUE | Дедупликация исполнителей при загрузке |
| `albums (artist_id, normalized_title)` UNIQUE | Один альбом с таким названием у одного исполнителя |
| `tracks.content_hash` UNIQUE | Тот же файл нельзя загрузить дважды |
| `tracks.file_path` UNIQUE | Два трека не могут указывать на один файл |
| `playlist_tracks (playlist_id, track_id)` UNIQUE | Устраняет гонку `MAX(position)+1` — см. [`04`](04-domain-model.md) |
| `refresh_tokens.token_hash` UNIQUE | Поиск токена при обновлении |
| `users.username` UNIQUE | Вход |
| `outbound_jobs.dedupe_key` UNIQUE | Дедупликация скробблов ([ADR-0023](adr/0023-generic-outbox.md)) |
| `outbound_jobs (state, next_attempt_at)` | Рабочий запрос воркера «что уже пора выполнять» |
| `playlists (is_public, updated_at)` | Витрина публичных плейлистов |
| `listening_stats` PK `(user_id, hour, track_id)` | Ключ **и есть** единица сводки; запрос статистики — диапазон часов одного пользователя, поэтому отдельный индекс под чтение не нужен |
| GIN `gin_trgm_ops` × 4 | Подстрочный поиск ([ADR-0010](adr/0010-search-rank-in-sql.md)) |

Обратите внимание на `listening_stats.track_id`: он существует **не для чтения**, а для обратного
направления — очистки при удалении трека. Такие пояснения есть в комментариях к конфигурациям, и их
стоит писать при добавлении новых индексов.

## Миграции

Их всего две:

| Миграция | Содержание |
|---|---|
| `20260816172802_InitialSchema` | Вся схема + ручной SQL: `pg_trgm`, четыре GIN-индекса, функция `search_rank` |
| `20260817074113_UniquePlaylistTrack` | Чистит дубли, перенумеровывает позиции, создаёт уникальный индекс |

Две вещи, которые стоит из них усвоить.

**Ручной SQL в миграции — нормально.** Класс операторов `gin_trgm_ops` и SQL-функция в модели EF не
выражаются. Они создаются через `migrationBuilder.Sql(...)` в приватном методе `CreateSearchObjects`
с развёрнутым комментарием.

**Миграция может чинить данные, а не только схему.** `UniquePlaylistTrack` не может просто создать
уникальный индекс — он упадёт на существующих дублях. Поэтому она сначала их удаляет и
перенумеровывает позиции. Так и надо делать: миграция обязана быть применимой к живой базе.

**`Down` пишется.** В `InitialSchema` он удаляет функцию, но **оставляет расширение `pg_trgm`**: от
него могут зависеть другие объекты, и удаление общего расширения куда разрушительнее.

### Как добавить миграцию

```bash
cd backend
dotnet ef migrations add ИмяМиграции \
    --project src/MusicStreaming.Infrastructure \
    --startup-project src/MusicStreaming.Api
```

Пакет `Microsoft.EntityFrameworkCore.Design` уже подключён в `MusicStreaming.Api`, `MigrationsAssembly`
указывает на Infrastructure. Применять руками не нужно — приложение мигрирует само при старте
([ADR-0011](adr/0011-migrate-on-startup.md)).

**Перед коммитом** откройте сгенерированный файл и прочитайте его. EF регулярно предлагает пересоздать
индекс или удалить и добавить колонку вместо переименования — на живой базе это потеря данных.

## Как писать запросы

### Проекции

Никогда не поднимайте сущность целиком, чтобы отдать три поля. Проекции —
[`Common/Projections.cs`](../../backend/src/MusicStreaming.Application/Common/Projections.cs),
подставляются в `Select(...)` и уезжают в SQL:

```csharp
var albums = await db.Albums
    .Where(a => a.ArtistId == artistId)
    .Select(Projections.Album())
    .ToListAsync(ct);
```

### Пагинация

[`Common/Paging.cs`](../../backend/src/MusicStreaming.Application/Common/Paging.cs) —
одно расширение на всё приложение:

```csharp
return await query.ToPagedAsync(page, Projections.Track(currentUser.Id), ct);
```

Оно само считает `Count`, применяет `Skip`/`Take` и собирает `PagedResult<T>`. `PageRequest`
нормализует входные параметры и **зажимает размер страницы**, поэтому `?pageSize=1000000` не уронит
сервер.

### Точечная загрузка по идентификаторам

[`Common/ProjectionLookups.cs`](../../backend/src/MusicStreaming.Application/Common/ProjectionLookups.cs)
— для шаблона «сначала получили список id, потом одним запросом подняли данные»:

```csharp
var tracks = await db.TracksByIdAsync(userId, ids, ct);
```

На этом держится [ADR-0021](adr/0021-cache-stores-ids-only.md).

### `AsNoTracking` для чтения

Там, где сущность не будет изменяться, ставьте `AsNoTracking()` — так делает, например,
`StreamingService`. Change tracker не будет держать копию.

### Сырой SQL

Допустим, когда LINQ не выражает нужное — прежде всего в `StatisticsService` с `AT TIME ZONE`. Идёт
через `db.Database` (см. [ADR-0004](adr/0004-efcore-in-application-layer.md)), результат — keyless-тип.
Параметры передавайте параметрами, а не конкатенацией.

## Единица работы

`SaveChangesAsync()` — и есть транзакция. Явных транзакций в коде почти нет, потому что почти каждый
сценарий укладывается в одно сохранение.

Исключение — загрузка пакета файлов, где нужно продолжить после отвергнутого файла. Там используется
`ChangeTracker` для отбрасывания незакоммиченных сущностей; подробности — в
[`07-media-pipeline.md`](07-media-pipeline.md).

## Куда дальше

[`06-security.md`](06-security.md) — вход, токены и права.
