using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Infrastructure.Recommendations;
using Xunit;

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Прогоняет весь путь сигнала: клиент о нём сообщил, он записан в журнал событий, свёрнут в профиль
/// вкуса, превращён в кандидатов, отранжирован в полки и прочитан обратно через API.
/// </summary>
[Collection(nameof(RecommendationApiCollection))]
public class RecommendationPipelineTests(RecommendationApiFixture fixture)
{
    private static readonly TimeSpan IngestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Reported_events_are_stored_and_shape_the_profile()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();

        await PostEventsAsync(client,
            Completed(library.Track(0)),
            Liked(library.Track(0)),
            Completed(library.Track(1)),
            Skipped(library.Track(10), listened: 3));

        await WaitForEventsAsync(4);
        await RollupAsync(library.UserId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.UserTasteProfiles.AsNoTracking()
            .FirstAsync(p => p.UserId == library.UserId, Cancel.Token);

        // Дослушано, лайк, дослушано — пропуск несёт отрицательный вес и здесь не учитывается.
        Assert.Equal(3, profile.PositiveSignalCount);
        Assert.Equal(4, profile.TotalEventCount);
        Assert.Equal(3, profile.DistinctTracks);
        Assert.True(profile.SkipRate > 0);

        var loved = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.UserId == library.UserId && a.TrackId == library.Track(0), Cancel.Token);

        var rejected = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.UserId == library.UserId && a.TrackId == library.Track(10), Cancel.Token);

        Assert.True(loved.Score > 0.4, $"A completed and liked track scored {loved.Score}");
        Assert.True(rejected.Score < 0, $"An abandoned track scored {rejected.Score}");
        Assert.Equal(1, rejected.SkipCount);
    }

    /// <summary>
    /// Свойство, благодаря которому роллап безопасно перезапускать после падения и безопасно ставить
    /// в расписание часто: второй проход по тем же событиям не должен менять ничего.
    /// </summary>
    [Fact]
    public async Task Rolling_up_twice_does_not_double_count()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();

        await PostEventsAsync(client, Completed(library.Track(0)), Completed(library.Track(1)));
        await WaitForEventsAsync(2);

        await RollupAsync(library.UserId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var first = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.TrackId == library.Track(0), Cancel.Token);

        await RollupAsync(library.UserId);

        var second = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.TrackId == library.Track(0), Cancel.Token);

        Assert.Equal(first.PlayCount, second.PlayCount);
        Assert.Equal(first.DecayedWeight, second.DecayedWeight, precision: 10);
        Assert.Equal(first.CompletionSamples, second.CompletionSamples);
    }

    [Fact]
    public async Task A_listening_history_produces_personalised_shelves()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();

        // Явное предпочтение одного исполнителя и явное отвержение другого.
        await PostEventsAsync(client,
            Completed(library.Track(0)),
            Completed(library.Track(1)),
            Completed(library.Track(2)),
            Liked(library.Track(1)),
            Skipped(library.Track(15), listened: 2),
            Skipped(library.Track(16), listened: 3));

        await WaitForEventsAsync(6);
        await BuildEverythingAsync(library.UserId);

        var home = await client.GetFromJsonAsync<RecommendationHomeDto>(
            "/api/recommendations/home?sectionSize=12", Cancel.Token);

        Assert.NotNull(home);
        Assert.False(home.IsColdStart);
        Assert.NotEmpty(home.Sections);

        // Каждая полка должна быть отрисовываемой: заголовок, под которым пусто, — это дефект, а не
        // полка.
        Assert.All(home.Sections, section =>
        {
            var count = (section.Tracks?.Count ?? 0) + (section.Artists?.Count ?? 0) + (section.Albums?.Count ?? 0);
            Assert.True(count > 0, $"Shelf {section.Key} came back empty");
            Assert.False(string.IsNullOrWhiteSpace(section.BaseKey));
        });

        var recommended = home.Sections
            .Where(s => s.Tracks is not null)
            .SelectMany(s => s.Tracks!)
            .ToList();

        Assert.NotEmpty(recommended);
        Assert.All(recommended, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason.Kind)));

        // Оценки релевантности — отладочный вывод и до обычного читателя доходить не должны.
        Assert.All(recommended, item => Assert.Null(item.Score));

        var forYou = home.Sections.FirstOrDefault(s => s.BaseKey == ShelfKeys.ForYou);
        Assert.NotNull(forYou);
        Assert.NotNull(forYou.Tracks);

        // Понравившееся стоит выше брошенного. Намеренно не «пропущенного исполнителя нет вовсе»:
        // два пропуска — это склонность, а не запрет, и банить исполнителя по таким уликам — ровно
        // та чрезмерная реакция, от которой настроены уберечь штрафные веса.
        var ordered = forYou.Tracks!.Select(item => item.Track).ToList();

        var firstPreferred = ordered.FindIndex(track => track.ArtistId == library.Artist(0));
        var firstRejected = ordered.FindIndex(
            track => track.Id == library.Track(15) || track.Id == library.Track(16));

        Assert.True(firstPreferred >= 0, "Nothing from the artist the listener played made the shelf");
        Assert.True(
            firstRejected < 0 || firstPreferred < firstRejected,
            "An abandoned track outranked the artist the listener actually played");
    }

    /// <summary>
    /// Один исполнитель не может занять всю полку, как бы сильно слушатель его ни любил.
    ///
    /// <para>
    /// Библиотека намеренно засеяна широкой. Ограничение — это предпочтение, а не инвариант: когда
    /// исполнителей действительно не хватает на двенадцать мест, движок выбирает однообразную полку
    /// вместо куцей, и узкая библиотека проверяла бы этот запасной путь, а не само правило.
    /// </para>
    /// </summary>
    [Fact]
    public async Task No_single_artist_dominates_a_shelf()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync(artistCount: 14, tracksPerArtist: 4);

        // Всё от одного исполнителя, дослушанное до конца.
        var events = Enumerable.Range(0, 5).Select(index => Completed(library.Track(index))).ToArray();
        await PostEventsAsync(client, events);
        await WaitForEventsAsync(events.Length);
        await BuildEverythingAsync(library.UserId);

        var home = await client.GetFromJsonAsync<RecommendationHomeDto>(
            "/api/recommendations/home?sectionSize=12", Cancel.Token);

        var forYou = home!.Sections.First(s => s.BaseKey == ShelfKeys.ForYou);

        var perArtist = forYou.Tracks!
            .GroupBy(item => item.Track.ArtistId)
            .Select(group => group.Count())
            .Max();

        using var scope = fixture.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Application.Options.RecommendationOptions>>();

        var breakdown = string.Join(", ", forYou.Tracks!
            .GroupBy(item => item.Track.ArtistName)
            .Select(group => $"{group.Key}={group.Count()}"));

        Assert.True(
            perArtist <= options.Value.MaxPerArtist,
            $"One artist took {perArtist} of {forYou.Tracks!.Count} slots ({breakdown})");
    }

    [Fact]
    public async Task A_user_with_no_history_still_gets_a_home_page()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        await BuildEverythingAsync(library.UserId);

        var home = await client.GetFromJsonAsync<RecommendationHomeDto>(
            "/api/recommendations/home?sectionSize=12", Cancel.Token);

        Assert.NotNull(home);
        Assert.True(home.IsColdStart);
        Assert.NotEmpty(home.Sections);

        // Когда личного не на что опереться, полки приходится брать из самой библиотеки.
        Assert.Contains(
            home.Sections,
            section => section.BaseKey is ShelfKeys.NewReleases or ShelfKeys.Discover or ShelfKeys.Popular);
    }

    /// <summary>
    /// Неудобная середина: истории почти нет вовсе. Тянуть приходится похожести по содержанию, и
    /// страница не должна скатиться в пустоту.
    /// </summary>
    [Fact]
    public async Task A_user_with_a_single_play_gets_recommendations()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();

        await PostEventsAsync(client, Completed(library.Track(0)));
        await WaitForEventsAsync(1);
        await BuildEverythingAsync(library.UserId);

        var home = await client.GetFromJsonAsync<RecommendationHomeDto>(
            "/api/recommendations/home?sectionSize=12", Cancel.Token);

        Assert.NotNull(home);
        Assert.NotEmpty(home.Sections);

        var tracks = home.Sections.Where(s => s.Tracks is not null).SelectMany(s => s.Tracks!).ToList();
        Assert.NotEmpty(tracks);
    }

    [Fact]
    public async Task Only_skipping_does_not_break_the_page()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();

        var events = Enumerable.Range(0, 12)
            .Select(index => Skipped(library.Track(index), listened: 2))
            .ToArray();

        await PostEventsAsync(client, events);
        await WaitForEventsAsync(events.Length);
        await BuildEverythingAsync(library.UserId);

        var response = await client.GetAsync("/api/recommendations/home?sectionSize=12", Cancel.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var home = await response.Content.ReadFromJsonAsync<RecommendationHomeDto>(Cancel.Token);
        Assert.NotNull(home);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var negative = await db.UserTrackAffinities.AsNoTracking()
            .CountAsync(a => a.UserId == library.UserId && a.Score < 0, Cancel.Token);

        Assert.Equal(12, negative);
    }

    // ── Вспомогательное ─────────────────────────────────────────────────────────────────────

    private async Task<(SeededLibrary Library, HttpClient Client)> StartAsync(
        int artistCount = 4, int tracksPerArtist = 5)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var library = await LibrarySeeder.SeedAsync(db, artistCount, tracksPerArtist);
        var client = await fixture.CreateSignedInClientAsync();

        return (library, client);
    }

    private static object Completed(Guid trackId) => new
    {
        type = "trackCompleted",
        trackId,
        occurredAt = DateTimeOffset.UtcNow,
        positionSeconds = 200,
        listenedSeconds = 200,
        durationSeconds = 200,
        sessionId = Session,
        source = "home",
        platform = "web",
    };

    private static object Skipped(Guid trackId, int listened) => new
    {
        type = "trackSkipped",
        trackId,
        occurredAt = DateTimeOffset.UtcNow,
        positionSeconds = listened,
        listenedSeconds = listened,
        durationSeconds = 200,
        sessionId = Session,
        source = "home",
        platform = "web",
    };

    private static object Liked(Guid trackId) => new
    {
        type = "trackLiked",
        trackId,
        occurredAt = DateTimeOffset.UtcNow,
        sessionId = Session,
        source = "home",
        platform = "web",
    };

    private static readonly Guid Session = Guid.CreateVersion7();

    private static async Task PostEventsAsync(HttpClient client, params object[] events)
    {
        var response = await client.PostAsJsonAsync("/api/events", new { events });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    /// <summary>
    /// Приём асинхронен по замыслу: запрос возвращается раньше записи. Тесты ждут журнала, а не
    /// спят, поэтому они и не мигают, и не раздувают набор фиксированными задержками.
    /// </summary>
    private async Task WaitForEventsAsync(int expected)
    {
        var deadline = DateTimeOffset.UtcNow + IngestTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (await db.PlaybackEvents.CountAsync() >= expected)
                return;

            await Task.Delay(100);
        }

        Assert.Fail($"Only {await CountEventsAsync()} of {expected} events were written within {IngestTimeout}.");
    }

    private async Task<int> CountEventsAsync()
    {
        using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .PlaybackEvents.CountAsync();
    }

    private async Task RollupAsync(Guid userId)
    {
        using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProfileRollupService>().RollupAsync(userId);
    }

    /// <summary>Прогоняет все стадии фоновых воркеров в том порядке, в каком их прогнали бы они.</summary>
    private async Task BuildEverythingAsync(Guid userId)
    {
        using var scope = fixture.CreateScope();
        var provider = scope.ServiceProvider;

        await provider.GetRequiredService<ProfileRollupService>().RollupAsync(userId);

        var maintenance = provider.GetRequiredService<SimilarityMaintenance>();
        await maintenance.RefreshTrackStatsAsync();
        await maintenance.RefreshSimilarityAsync();

        await provider.GetRequiredService<ShelfGenerationService>()
            .GenerateAsync(userId, Guid.CreateVersion7());
    }
}
