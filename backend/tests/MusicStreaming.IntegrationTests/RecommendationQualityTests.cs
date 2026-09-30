// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.IntegrationTests.Evaluation;
using Xunit;

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Оффлайн-оценка: история за N дней делится по времени, профиль строится только на прошлом, а
/// отложенные дни служат ответом. Без неё любая настройка весов — угадывание.
/// </summary>
[Collection(nameof(RecommendationApiCollection))]
public class RecommendationQualityTests(RecommendationApiFixture fixture, ITestOutputHelper output)
{
    private const int K = 24;
    private const int ShelfK = 12;
    private const int TrainDays = 30;
    private const int HeldOutDays = 7;

    private static readonly (string Username, int Scene)[] Companions =
    [
        ("eval-neighbour-a", 0),
        ("eval-neighbour-b", 0),
        ("eval-stranger", 1),
    ];

    [Fact]
    public async Task Recommendation_quality_is_measured_against_a_popularity_baseline()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var companionIds = await EnsureCompanionsAsync();

        EvaluationCatalog catalog;
        Dictionary<Guid, int> durations;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            catalog = await EvaluationLibrary.SeedAsync(db, artistsPerScene: 8, tracksPerArtist: 10);
            durations = await db.Tracks.AsNoTracking()
                .ToDictionaryAsync(track => track.Id, track => track.DurationSeconds, Cancel.Token);
        }

        var userId = await OwnerIdAsync();
        var home = catalog.Scenes[0];

        // Окна отсчитываются от полуночи: иначе срез train/test падал бы каждый раз в другую точку
        // суток, и метрики гуляли бы от времени запуска, а не от ранжирования.
        var now = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var start = now.AddDays(-(TrainDays + HeldOutDays));
        var cutoff = now.AddDays(-HeldOutDays);

        var history = SyntheticHistory.Generate(catalog, home, start, now, seed: 20260826);
        var train = history.Where(play => play.OccurredAt < cutoff).ToList();
        var heldOut = history.Where(play => play.OccurredAt >= cutoff).ToList();

        var known = train.Select(play => play.TrackId).ToHashSet();

        // Ответ — только то, что человек услышал впервые уже после среза: угадать сыгранное
        // раньше рекомендациям и не нужно, они его как раз штрафуют.
        var answer = heldOut
            .Where(play => play.Completed && !known.Contains(play.TrackId))
            .Select(play => play.TrackId)
            .ToHashSet();

        await WriteHistoryAsync(userId, train, durations, catalog, companionIds, start, cutoff);

        Assert.True(answer.Count >= 5, $"The harness produced only {answer.Count} held-out discoveries");

        // Звучание — единственная похожесть в подсистеме, так что каталог мерится с загруженным
        // индексом: без него оценка видела бы только метаданные и популярность.
        await fixture.ReloadEmbeddingIndexAsync();
        await fixture.BuildRecommendationsAsync(userId);

        var feed = await fixture.HomeAsync(userId, 12);

        Assert.NotNull(feed);

        // Все полки одним списком, лучший скор трека первым: так ранжированы и полки, и пул микса.
        List<RecommendationCacheEntry> shelves;
        using (var scope = fixture.CreateScope())
        {
            shelves = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().RecommendationCache
                .AsNoTracking().Where(shelf => shelf.UserId == userId).ToListAsync(Cancel.Token);
        }

        var shelved = shelves
            .SelectMany(shelf => shelf.Payload)
            .Where(item => item.Kind == RecommendedItemKind.Track)
            .OrderByDescending(item => item.Score)
            .Select(item => item.ItemId)
            .Distinct();

        // Сравнивается только то, что предлагается впервые: полки намеренно содержат и знакомое
        // («продолжить», «вспомнить»), а базовая линия знакомое исключает.
        var forYou = Unheard(Shelf(feed, ShelfKeys.ForYou), known);
        var discover = Unheard(Shelf(feed, ShelfKeys.Discover), known);
        var everything = Unheard(shelved, known);

        var baseline = await PopularityBaselineAsync(known);

        var ranked = RecommendationEvaluator.Measure("forYou", forYou, answer, home, catalog, ShelfK);
        var discovery = RecommendationEvaluator.Measure("discover", discover, answer, home, catalog, ShelfK);
        var flattened = RecommendationEvaluator.Measure("all shelves", everything, answer, home, catalog, K);
        var naive = RecommendationEvaluator.Measure("popularity", baseline, answer, home, catalog, K);

        output.WriteLine(
            $"library={catalog.TrackCount} tracks, train={train.Count} plays, answer={answer.Count} tracks");

        foreach (var quality in new[] { ranked, discovery, flattened, naive })
            output.WriteLine(quality.Row());

        Assert.NotEmpty(everything);

        // Качество — recall против популярности, доля своей сцены, разброс по артистам — только
        // печатается. Подсистему сознательно упростили ценой этих цифр, и валить на них сборку
        // значило бы спорить с этим решением на каждом прогоне. Смотреть на них — при правке весов.
        await AssertEmbeddedTracksAreNotFavouredAsync(everything, output);

        // Разброс меряется на том же префиксе, по которому считается recall: весь список для этого
        // не годится — в нём пул микса дня, и доля разных артистов в нём падает от одного размера.
        await ReportArtistSpreadAsync([.. everything.Take(K)], output);
    }

    /// <summary>
    /// Доля треков без вектора в ленте должна примерно совпадать с их долей в библиотеке.
    /// <para>
    /// Это прямая проверка перенормировки в <c>RankingWeights.Combine</c>, и она падает
    /// <b>в обе стороны</b>. Пока идёт дозаполнение, у половины библиотеки вектора нет; если
    /// заэмбежженные начнут выигрывать самим фактом наличия терма, лента перекосится — но
    /// перенаграждать незаэмбежженные так же неверно.
    /// </para>
    /// </summary>
    private async Task AssertEmbeddedTracksAreNotFavouredAsync(
        IReadOnlyList<Guid> feed, ITestOutputHelper output)
    {
        const double Tolerance = 0.10;

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var embedded = await db.TrackEmbeddings.AsNoTracking()
            .Where(item => item.Succeeded)
            .Select(item => item.TrackId)
            .ToListAsync(Cancel.Token);

        var embeddedSet = embedded.ToHashSet();
        var libraryTotal = await db.Tracks.AsNoTracking().CountAsync(Cancel.Token);

        var inLibrary = 1.0 - embeddedSet.Count / (double)libraryTotal;
        var inFeed = feed.Count(id => !embeddedSet.Contains(id)) / (double)feed.Count;

        output.WriteLine(
            $"unembedded    library={inLibrary:P1}  feed={inFeed:P1}  (tolerance {Tolerance:P0})");

        Assert.True(
            Math.Abs(inFeed - inLibrary) <= Tolerance,
            $"Tracks without an embedding make up {inLibrary:P1} of the library but {inFeed:P1} "
            + "of the feed — the missing-signal weight is not being redistributed evenly");
    }

    /// <summary>
    /// Сколько разных артистов в ленте: набить её одним любимым артистом — верный способ завысить
    /// recall, и эта строка показывает, не за счёт ли этого выросли цифры.
    /// </summary>
    private async Task ReportArtistSpreadAsync(IReadOnlyList<Guid> feed, ITestOutputHelper output)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var artists = await db.Tracks.AsNoTracking()
            .Where(track => feed.Contains(track.Id))
            .Select(track => track.ArtistId)
            .ToListAsync(Cancel.Token);

        var coverage = artists.Distinct().Count() / (double)feed.Count;

        output.WriteLine($"artist spread  {artists.Distinct().Count()}/{feed.Count} = {coverage:P1}");
    }

    private static IReadOnlyList<Guid> Shelf(RecommendationHomeDto home, string baseKey) =>
    [
        .. home.Sections
            .Where(section => section.BaseKey == baseKey && section.Tracks is not null)
            .SelectMany(section => section.Tracks!)
            .Select(item => item.Track.Id),
    ];

    private static List<Guid> Unheard(IEnumerable<Guid> ranked, IReadOnlySet<Guid> known) =>
        ranked.Where(trackId => !known.Contains(trackId)).ToList();

    private async Task WriteHistoryAsync(
        Guid userId,
        IReadOnlyList<SyntheticPlay> train,
        IReadOnlyDictionary<Guid, int> durations,
        EvaluationCatalog catalog,
        IReadOnlyList<Guid> companionIds,
        DateTimeOffset start,
        DateTimeOffset cutoff)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.PlaybackEvents.AddRange(SyntheticHistory.ToEvents(userId, train, durations));

        // Соседи по вкусу и чужак: без них популярность повторяла бы историю самого слушателя,
        // и наивная базовая линия была бы выиграна даром.
        for (var index = 0; index < Companions.Length; index++)
        {
            var scene = catalog.Scenes[Companions[index].Scene % catalog.Scenes.Count];
            var plays = SyntheticHistory.Generate(catalog, scene, start, cutoff, seed: 1000 + index);

            db.PlaybackEvents.AddRange(SyntheticHistory.ToEvents(companionIds[index], plays, durations));
        }

        await db.SaveChangesAsync(Cancel.Token);
    }

    private async Task<List<Guid>> PopularityBaselineAsync(IReadOnlySet<Guid> known)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var ranked = await db.TrackStats.AsNoTracking()
            .OrderByDescending(stats => stats.PopularityScore)
            .ThenBy(stats => stats.TrackId)
            .Select(stats => stats.TrackId)
            .ToListAsync(Cancel.Token);

        return ranked.Where(trackId => !known.Contains(trackId)).Take(K).ToList();
    }

    private async Task<List<Guid>> EnsureCompanionsAsync()
    {
        foreach (var (username, _) in Companions)
            await fixture.CreateSignedInClientAsync(username, "Companion!2026");

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var names = Companions.Select(companion => companion.Username).ToList();
        var byName = await db.Users.AsNoTracking()
            .Where(user => names.Contains(user.Username))
            .ToDictionaryAsync(user => user.Username, user => user.Id, Cancel.Token);

        return [.. Companions.Select(companion => byName[companion.Username])];
    }

    private async Task<Guid> OwnerIdAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Users.AsNoTracking().OrderBy(user => user.CreatedAt).Select(user => user.Id)
            .FirstAsync(Cancel.Token);
    }
}
