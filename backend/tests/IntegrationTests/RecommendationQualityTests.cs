// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Globalization;
using System.Numerics.Tensors;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using App.Dtos;
using Domain.Entities.Recommendations;
using Infrastructure.Persistence;
using IntegrationTests.Evaluation;
using Xunit;
using App.Recommendations;
using App.Recommendations.Embeddings;
using App.Recommendations.Home;

namespace IntegrationTests;

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

        var catalog = await SeedCatalogAsync(sceneCount: 3);
        var home = catalog.Scenes[0];

        var run = await RunAsync(catalog, (start, now) =>
            SyntheticHistory.Generate(catalog, home, start, now, seed: 20260826));

        var ranked = RecommendationEvaluator.Measure("forYou", run.ForYou, run.Answer, home, catalog, ShelfK);
        var discovery = RecommendationEvaluator.Measure("discover", run.Discover, run.Answer, home, catalog, ShelfK);
        var flattened = RecommendationEvaluator.Measure("all shelves", run.Everything, run.Answer, home, catalog, K);
        var naive = RecommendationEvaluator.Measure("popularity", run.Baseline, run.Answer, home, catalog, K);

        output.WriteLine(
            $"library={catalog.TrackCount} tracks, train={run.TrainCount} plays, answer={run.Answer.Count} tracks");

        foreach (var quality in new[] { ranked, discovery, flattened, naive })
            output.WriteLine(quality.Row());

        output.WriteLine($"familiar      forYou={run.FamiliarShare:P0} of {run.ForYouShelf.Count}");

        Assert.NotEmpty(run.Everything);

        // «Для вас» не повторяет ротацию слушателя, а новое на ней — в основном из его сцены.
        Assert.True(run.FamiliarShare <= RecommendationTuning.Shelves.ForYouFamiliarShare + 0.01,
            $"forYou is {run.FamiliarShare:P0} familiar");
        Assert.True(ranked.HomeSceneShare >= 0.3, $"Only {ranked.HomeSceneShare:P0} of new forYou picks are from the home scene");
        Assert.True(flattened.Hits >= naive.Hits - 1,
            $"The shelves found {flattened.Hits} discoveries, popularity alone finds {naive.Hits}");

        await AssertEmbeddedTracksAreNotFavouredAsync(run.Everything, output);

        await ReportArtistSpreadAsync([.. run.Everything.Take(K)], output);
    }

    // Слушатель с двумя непохожими вкусами (55% и 30%) и сценой-мостом, которая звучит посередине
    // между ними и которую он никогда не включал. Средний вектор вкуса указывает ровно на мост.
    [Fact]
    public async Task Two_tastes_are_served_without_drifting_to_the_sound_between_them()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var catalog = await SeedCatalogAsync(sceneCount: 4, bridge: (0, 1));
        var (major, minor, noise, bridge) = (catalog.Scenes[0], catalog.Scenes[1], catalog.Scenes[2], catalog.Scenes[3]);
        var tastes = new HashSet<EvaluationScene?> { major, minor };

        var run = await RunAsync(catalog, (start, now) =>
            SyntheticHistory.Generate([(major, 0.55), (minor, 0.30)], [noise], start, now, seed: 20261006));

        var minorAnswer = run.Answer.Where(trackId => catalog.SceneOf(trackId) == minor).ToHashSet();

        output.WriteLine(
            $"library={catalog.TrackCount} tracks, train={run.TrainCount} plays, answer={run.Answer.Count} tracks "
            + $"({minorAnswer.Count} from the minor taste)");

        foreach (var (name, ranked, k) in new[]
                 {
                     ("forYou", run.ForYou, ShelfK),
                     ("all shelves", run.Everything, K),
                     ("popularity", run.Baseline, K),
                 })
        {
            var overall = RecommendationEvaluator.Measure(name, ranked, run.Answer, major, catalog, k);
            var second = RecommendationEvaluator.Measure(name, ranked, minorAnswer, minor, catalog, k);

            output.WriteLine(
                $"{overall.Row()}  minor recall={second.Recall:F3}  {Shares([.. ranked.Take(k)], catalog)}");
        }

        output.WriteLine($"familiar      forYou={run.FamiliarShare:P0} of {run.ForYouShelf.Count}");

        // Радио выбирает якорь случайно, поэтому его доли только печатаются.
        var radio = await RadioAsync(batches: 6, size: 10);
        output.WriteLine($"radio         {radio.Count} tracks  {Shares(radio, catalog)}");

        Assert.NotEmpty(run.Everything);

        var taste = await TasteAsync();
        output.WriteLine("taste modes   " + string.Join("  ", taste.Modes.Select(mode =>
            string.Create(CultureInfo.InvariantCulture, $"{mode.Share:F2} → {NearestScene(mode.Centre, catalog.Scenes)?.Name}"))));

        // Два вкуса — два центра, каждый у своей сцены, и ни один не у моста между ними.
        Assert.True(taste.Modes.Count >= 2, $"The taste collapsed into {taste.Modes.Count} centre");
        Assert.Contains(NearestScene(taste.Modes[0].Centre, catalog.Scenes), tastes);
        Assert.Contains(NearestScene(taste.Modes[1].Centre, catalog.Scenes), tastes);
        Assert.NotEqual(NearestScene(taste.Modes[0].Centre, catalog.Scenes), NearestScene(taste.Modes[1].Centre, catalog.Scenes));
        Assert.DoesNotContain(taste.Modes, mode => NearestScene(mode.Centre, catalog.Scenes) == bridge);

        // Новое на «Для вас» — из обоих вкусов, а не из моста или шума.
        var forYouTop = run.ForYou.Take(ShelfK).ToList();
        Assert.Contains(forYouTop, trackId => catalog.SceneOf(trackId) == minor);
        Assert.True(run.FamiliarShare <= RecommendationTuning.Shelves.ForYouFamiliarShare + 0.01,
            $"forYou is {run.FamiliarShare:P0} familiar");
    }

    private async Task<TasteModel> TasteAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var snapshot = scope.ServiceProvider.GetRequiredService<EmbeddingIndex>().Snapshot();

        var context = await UserRecommendationContext.LoadAsync(
            db, snapshot, await OwnerIdAsync(), DateTimeOffset.UtcNow, Cancel.Token);

        return context.Taste;
    }

    // Сцена, к трекам которой центр ближе всего в среднем.
    private EvaluationScene? NearestScene(float[] centre, IReadOnlyList<EvaluationScene> scenes)
    {
        var snapshot = fixture.Services.GetRequiredService<EmbeddingIndex>().Snapshot();

        return scenes
            .Select(scene => (scene, Similarity: scene.TrackIds
                .Select(snapshot.RowOf)
                .Where(row => row >= 0)
                .Select(row => (double)TensorPrimitives.Dot(snapshot.Vector(row), centre))
                .DefaultIfEmpty(double.NegativeInfinity)
                .Average()))
            .MaxBy(pair => pair.Similarity)
            .scene;
    }

    private record EvaluationRun(
        int TrainCount,
        IReadOnlySet<Guid> Answer,
        IReadOnlyList<Guid> ForYouShelf,
        IReadOnlyList<Guid> ForYou,
        IReadOnlyList<Guid> Discover,
        IReadOnlyList<Guid> Everything,
        IReadOnlyList<Guid> Baseline,
        double FamiliarShare);

    private async Task<EvaluationCatalog> SeedCatalogAsync(int sceneCount, (int Left, int Right)? bridge = null)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await EvaluationLibrary.SeedAsync(
            db, sceneCount, artistsPerScene: 8, tracksPerArtist: 10, bridge: bridge);
    }

    // Обучение — 30 дней истории, ответ — треки, впервые дослушанные в следующие 7 дней.
    private async Task<EvaluationRun> RunAsync(
        EvaluationCatalog catalog, Func<DateTimeOffset, DateTimeOffset, List<SyntheticPlay>> generate)
    {
        var companionIds = await EnsureCompanionsAsync();

        Dictionary<Guid, int> durations;
        using (var scope = fixture.CreateScope())
        {
            durations = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Tracks.AsNoTracking()
                .ToDictionaryAsync(track => track.Id, track => track.DurationSeconds, Cancel.Token);
        }

        var userId = await OwnerIdAsync();

        var now = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var start = now.AddDays(-(TrainDays + HeldOutDays));
        var cutoff = now.AddDays(-HeldOutDays);

        var history = generate(start, now);
        var train = history.Where(play => play.OccurredAt < cutoff).ToList();
        var heldOut = history.Where(play => play.OccurredAt >= cutoff).ToList();

        var known = train.Select(play => play.TrackId).ToHashSet();

        var answer = heldOut
            .Where(play => play.Completed && !known.Contains(play.TrackId))
            .Select(play => play.TrackId)
            .ToHashSet();

        await WriteHistoryAsync(userId, train, durations, catalog, companionIds, start, cutoff);

        Assert.True(answer.Count >= 5, $"The harness produced only {answer.Count} held-out discoveries");

        await fixture.ReloadEmbeddingIndexAsync();
        await fixture.BuildRecommendationsAsync(userId);

        var feed = await fixture.HomeAsync(userId, ShelfK);

        Assert.NotNull(feed);

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

        var forYouShelf = Shelf(feed, ShelfKeys.ForYou);

        return new EvaluationRun(
            train.Count,
            answer,
            forYouShelf,
            Unheard(forYouShelf, known),
            Unheard(Shelf(feed, ShelfKeys.Discover), known),
            Unheard(shelved, known),
            await PopularityBaselineAsync(known),
            forYouShelf.Count == 0 ? 0 : forYouShelf.Count(known.Contains) / (double)forYouShelf.Count);
    }

    private async Task<List<Guid>> RadioAsync(int batches, int size)
    {
        var client = await fixture.CreateSignedInClientAsync();
        var played = new List<Guid>();

        for (var batch = 0; batch < batches; batch++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/recommendations/radio", new RadioRequest(null, played, size), Cancel.Token);
            response.EnsureSuccessStatusCode();

            var next = (await response.Content.ReadFromJsonAsync<RadioBatchDto>(Cancel.Token))!;
            played.AddRange(next.Tracks.Select(item => item.Track.Id));
        }

        return played;
    }

    private static string Shares(IReadOnlyList<Guid> ranked, EvaluationCatalog catalog) =>
        ranked.Count == 0
            ? "(empty)"
            : string.Join("  ", catalog.Scenes.Select((scene, index) =>
                string.Create(CultureInfo.InvariantCulture,
                    $"s{index}={ranked.Count(trackId => catalog.SceneOf(trackId) == scene) / (double)ranked.Count:F2}")));

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

    private static IReadOnlyList<Guid> Shelf(IReadOnlyList<RecommendationSectionDto> home, string baseKey) =>
    [
        .. home
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
