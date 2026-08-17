# 12. Тесты

## Две стратегии, и никаких моков

В проекте **нет ни одной библиотеки мокирования** — ни Moq, ни NSubstitute, ни FakeItEasy — и нет
самописных заглушек. Ассерты — обычные `Assert.*` из xUnit v3, без FluentAssertions. Это осознанный
выбор, обоснование — [ADR-0026](adr/0026-no-mocks-real-postgres.md).

| Проект | Что проверяет | Нужен Docker |
|---|---|---|
| `MusicStreaming.UnitTests` | Чистые функции: без базы, без HTTP, без побочных эффектов | нет |
| `MusicStreaming.IntegrationTests` | Приложение целиком поверх настоящего PostgreSQL | **да** |

Правило выбора простое: **если у логики нет побочных эффектов — юнит-тест; во всех остальных случаях
— интеграционный.** Промежуточного варианта («сервис с подменёнными зависимостями») здесь нет.

## Запуск

```bash
make test
# = cd backend && dotnet test MusicStreaming.slnx --configuration Release

# только юнит-тесты, без Docker
dotnet test backend/tests/MusicStreaming.UnitTests/MusicStreaming.UnitTests.csproj

# один класс
dotnet test backend/MusicStreaming.slnx --filter "FullyQualifiedName~TrackDeleteTests"
```

> **Без Docker интеграционные тесты пропускаются, а не падают.** Прогон будет зелёным, но проверит
> примерно треть поведения. Перед отправкой PR убедитесь, что Docker запущен.

## Юнит-тесты

Десять файлов в корне плюс папка `Recommendations/`:

| Файл | Что покрывает |
|---|---|
| `NormalizeTests` | Ключ нормализации — от него зависят все уникальные индексы |
| `ArtistNamesTests` | Разбор «A feat. B / C» |
| `SearchTermTests` | Подготовка поискового запроса |
| `PageRequestTests` | Зажим границ страницы |
| `LyricsTextTests` | Разбор LRC |
| `AudioUploadTests` | Определение формата |
| `DownloadFileNameTests` | Очистка имени файла |
| `PlaybackRulesTests` | `ScrobbleRules`, `PlayAttempt` |
| `PlaybackSessionRegistryTests` | Вытеснение устройств |
| `Recommendations/*` | `AffinityMath`, `CandidateScorer`, `Diversifier`, `Explorer`, `EventWeights`, `RecencyDecay`, `PlaybackEventFactory` |

Папка `Recommendations/` — самая плотно покрытая часть проекта, и это прямое следствие архитектуры:
весь скоринг вынесен в чистые функции, которым контекст передаётся готовым
([`08-recommendations.md`](08-recommendations.md)). Есть готовый построитель данных
`Recommendations/CandidateBuilder.cs`.

## Интеграционные тесты

### Фикстура

Всё держится на
[`RecommendationApiFixture`](../../backend/tests/MusicStreaming.IntegrationTests/RecommendationApiFixture.cs)
— `WebApplicationFactory<Program>` плюс контейнер `postgres:17-alpine` через Testcontainers.

Каждое её решение стоит понимать, потому что все они получены из реальных падений:

**Один контейнер и один хост на весь прогон.** Через
`[CollectionDefinition]` + `ICollectionFixture`, все классы помечены
`[Collection(nameof(RecommendationApiCollection))]`. Поднимать Postgres на каждый класс было бы
неприемлемо долго.

**Пропуск, а не падение, без Docker.** Первая строка каждого теста:

```csharp
Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);
```

**Миграции прогоняются в `InitializeAsync`**, чтобы за них не платил первый тест.

**Приложение запускается как `Production`** с переопределениями:

| Настройка | Значение | Зачем |
|---|---|---|
| `ConnectionStrings:Default` | из контейнера | |
| `Jwt:SigningKey` | 50-символьная строка | Проходит проверку длины |
| `Owner:Username` / `Password` | `owner` / `integration-password` | |
| `Storage:RootPath` | временный каталог | Удаляется в `DisposeAsync` |
| `Recommendations:Enabled` | `false` | **Периодические воркеры выключены**, чтобы тест запускал роллап и генерацию явно, а не гонялся с таймером. Приём событий продолжает работать: он и сам входит в проверяемое, и он единственный писатель |
| `Transcode:Enabled` | `false` | ffmpeg в тестах не нужен |
| `Security:LoginAttemptsPerMinute` | `1000` | Все запросы набора идут с одного адреса, и боевые десять попыток делятся на всех |

**`https://localhost` и `HandleCookies = true`:**

```csharp
CreateClient(new WebApplicationFactoryClientOptions
{
    HandleCookies = true,
    BaseAddress = new Uri("https://localhost"),
});
```

Тестовый сервер не занимается TLS, но вне разработки приложение помечает cookie как `Secure`, а
контейнер cookie не отправит такие обратно по обычному http. Запросы приходили бы анонимными, и
**каждый** тест падал бы на авторизации, а не на том, что он проверяет.

**Клиенты входят один раз и кэшируются** — `CreateSignedInClientAsync()` для владельца,
`CreateSignedInClientAsync(username, password)` для обычного пользователя (создаётся владельцем через
`POST /api/admin/users`). Причина та же: вход ограничен по частоте, и набор, входящий на каждый тест,
упирался бы в 429.

**`DisposeAsync` объявлен через `new`, а не `override`** — и это не небрежность. `WithWebHostBuilder`
заводит производную фабрику, и её освобождение дошло бы до базового `DisposeAsync`, утащив за собой
контейнер с базой **посреди прогона**.

**Разбор JSON повторяет настройки API:**

```csharp
public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() },
};
```

Иначе тест проверял бы не контракт, а собственные настройки разбора.

### Изоляция данных

Тесты **делят одну базу и не сбрасывают её**. Изоляция — уникальными именами:

```csharp
private static string Unique(string prefix) => $"{prefix} {Guid.CreateVersion7():N}"[..24];
```

`LibrarySeeder.SeedAsync(db, artistCount: 4, tracksPerArtist: 5)` строит небольшую, но структурно
полную библиотеку: исполнители, альбомы с настоящей группировкой треков, два жанра и один совместный
трек — достаточно, чтобы похожести по содержанию было о чём говорить.

`LibrarySeeder.ClearAsync` **перечисляет таблицы вручную** в порядке внешних ключей, а не делает
`TRUNCATE CASCADE`. Это намеренно: новая таблица, не добавленная сюда, обнаружит себя падением, а не
тихим мусором между тестами.

`Cancel.Token` — прокси к `TestContext.Current.CancellationToken` (отмена на тест в xUnit v3),
существующий только ради длины строки: он нужен последним аргументом почти каждому вызову.

## Эталон стиля: `TrackDeleteTests`

[`TrackDeleteTests.cs`](../../backend/tests/MusicStreaming.IntegrationTests/TrackDeleteTests.cs) —
образец, на который стоит равняться.

**Имена-предложения:**

```csharp
A_batch_takes_its_files_albums_artists_and_genres_with_it
Ids_that_were_already_gone_come_back_as_missing_and_do_not_stop_the_rest
Bulk_delete_is_closed_to_everyone_but_administrators
```

Читая список упавших тестов, вы читаете список нарушенных обещаний.

**Комментарий объясняет, почему тест существует**, а не что он делает:

```csharp
/// Удалить уже удалённое — не ошибка: запрос шёл к тому состоянию, в котором библиотека и так
/// находится. Поэтому неизвестные идентификаторы возвращаются списком, а не отказом …
```

**Действие через HTTP, проверка через базу и диск.** Тест постит на `/api/tracks/bulk-delete`, затем
открывает scope, берёт `ApplicationDbContext` и `IMusicStorage` и проверяет, что строки исчезли **и
файл на диске тоже**. Именно это невозможно проверить моком.

**Настоящие загрузки:** синтетические mp3 с тегами (`AudioFormatTests.SyntheticMp3.Tagged`) уходят
как `MultipartFormDataContent`.

**Ассерт заодно извлекает:**

```csharp
var trackId = Assert.Single(uploaded.Uploaded).Id;
Assert.Equal(ghost, Assert.Single(result.Missing));
```

**Сообщение об ошибке содержит ответ:**

```csharp
Assert.True(response.IsSuccessStatusCode,
    $"unexpected status {response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel.Token)}");
```

**Константы из боевого кода, а не магические числа:** `TrackEditService.MaxBulkDelete + 1`.

## Что покрыто

| Область | Тесты |
|---|---|
| Загрузка и форматы | `UploadTests`, `UploadCheckTests`, `AudioFormatTests` (AAC/ALAC/FLAC, подмена расширения) |
| Удаление | `TrackDeleteTests` |
| Плейлисты | `PlaylistOrderTests`, `PublicPlaylistTests` |
| Аутентификация и админ | `RefreshTokenTests`, `AdminUserTests` |
| Поиск | `SearchRelevanceTests` |
| Воспроизведение | `ShuffleTests`, `RadioTests` |
| Рекомендации | `RecommendationApiTests`, `RecommendationPipelineTests`, `SimilarTracksTests` |
| Статистика | `StatisticsTests` (часовые пояса, идемпотентность роллапа) |
| Тексты песен | `LyricsTests` |
| Last.fm | `LastfmTests` |
| Наблюдаемость | `ExportedMetricNamesTests` |

Бинарные образцы — `Fixtures/aac.m4a`, `Fixtures/alac.m4a` (копируются в вывод). MP3 и FLAC
синтезируются кодом: так проще управлять тегами.

`ExportedMetricNamesTests` заслуживает упоминания отдельно — он закрепляет имена метрик, которые
экспортёр Prometheus однажды молча изменил, оставив пустые панели
([`10-observability.md`](10-observability.md)).

## Как написать новый тест

### Юнит-тест на скоринг

```csharp
[Fact]
public void Recently_played_track_is_pushed_down()
{
    var context = RankingContext.Empty(now);
    // … собрать кандидата через CandidateBuilder
    Assert.True(scoreAfter < scoreBefore);
}
```

Никаких моков не понадобится: весь контекст передаётся структурой.

### Интеграционный тест

```csharp
[Collection(nameof(RecommendationApiCollection))]
public class МойTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Понятное_предложение_о_том_что_гарантируется()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = Unique("Мой");           // уникальные данные — обязательно

        var response = await client.GetAsync($"/api/…", Cancel.Token);
        Assert.True(response.IsSuccessStatusCode, /* с телом ответа */);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // проверка состояния
    }
}
```

Чек-лист:

- [ ] `[Collection(nameof(RecommendationApiCollection))]` на классе;
- [ ] `Assert.SkipUnless` первой строкой;
- [ ] уникальные данные — тесты делят базу;
- [ ] `Cancel.Token` во все асинхронные вызовы;
- [ ] имя-предложение;
- [ ] XML-комментарий, если неочевидно, **зачем** тест;
- [ ] сообщение об ошибке содержит тело ответа.

## Ограничения, о которых нужно помнить

- **Тесты идут последовательно** внутри коллекции — параллелизма нет.
- **Состояние общее.** Забыли уникальное имя — получите плавающее падение, воспроизводящееся не
  всегда.
- **Периодические воркеры выключены.** Тест на фоновую обработку должен вызывать сервис напрямую
  через scope.
- **Покрытие не измеряется.** В CI нет ни сбора, ни выгрузки покрытия.
- **Сервис в изоляции не протестировать.** Любая проверка идёт через HTTP.

## Куда дальше

[`13-operations.md`](13-operations.md) — сборка и эксплуатация.
