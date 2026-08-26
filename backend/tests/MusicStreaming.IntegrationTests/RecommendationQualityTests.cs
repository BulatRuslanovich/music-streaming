// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Common;
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
    public async Task Personalised_ranking_beats_the_naive_baseline_on_held_out_days()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var listener = await fixture.CreateSignedInClientAsync();
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

        await fixture.BuildRecommendationsAsync(userId);

        var feed = await listener.GetFromJsonAsync<RecommendationHomeDto>(
            "/api/recommendations/home?sectionSize=12", Cancel.Token);

        Assert.NotNull(feed);

        var page = await listener.GetFromJsonAsync<PagedResult<RecommendedTrackDto>>(
            "/api/recommendations/tracks?page=1&pageSize=200", Cancel.Token);

        // Сравнивается только то, что предлагается впервые: полки намеренно содержат и знакомое
        // («продолжить», «вспомнить»), а базовая линия знакомое исключает.
        var forYou = Unheard(Shelf(feed, ShelfKeys.ForYou), known);
        var discover = Unheard(Shelf(feed, ShelfKeys.Discover), known);
        var everything = Unheard(page!.Items.Select(item => item.Track.Id), known);

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

        // Полки «Для вас» и «Discover» тоже в таблице, но утверждать по ним нечего: их незнакомая
        // часть — это слоты Explorer, который по определению выбирает за пределами привычного.
        // MAP печатается, но не проверяется: его разрывы зависят от порядка ничьих в пуле.
        Assert.True(
            flattened.Recall > naive.Recall,
            $"The feed fell behind a naive popularity baseline:\n{flattened.Row()}\n{naive.Row()}");

        // Домашняя сцена — треть библиотеки, так что попадание выше трети означает, что вкус понят.
        var chance = 1.0 / catalog.Scenes.Count;

        Assert.True(
            flattened.HomeSceneShare > chance,
            $"Only {flattened.HomeSceneShare:P0} of the feed came from the listener's own scene, "
            + $"chance alone gives {chance:P0}\n{flattened.Row()}");
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
