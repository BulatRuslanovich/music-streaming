// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Recommendations.Queue;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class QueueBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_empty_index_yields_an_empty_queue() =>
        Assert.Empty(Build(EmbeddingSnapshot.Empty, Request()));

    [Fact]
    public void The_queue_never_repeats_a_track()
    {
        var snapshot = Library(60);

        var queue = Build(snapshot, Request(size: 10));

        Assert.Equal(10, queue.Count);
        Assert.Equal(queue.Count, queue.Select(item => item.TrackId).Distinct().Count());
    }

    [Fact]
    public void The_currently_playing_track_never_comes_back()
    {
        var snapshot = Library(40);
        var current = snapshot.MetaAt(0).TrackId;

        var queue = Build(snapshot, Request(currentRow: 0, size: 12));

        Assert.DoesNotContain(queue, item => item.TrackId == current);
    }

    [Fact]
    public void Excluded_tracks_stay_out_even_when_they_fit_the_taste_best()
    {
        var snapshot = Library(40);
        var banned = Enumerable.Range(0, 20).Select(row => snapshot.MetaAt(row).TrackId).ToHashSet();

        var queue = Build(snapshot, Request(size: 10, exclude: banned));

        Assert.DoesNotContain(queue, item => banned.Contains(item.TrackId));
    }

    [Fact]
    public void No_more_than_two_tracks_of_one_artist()
    {
        // Пятьдесят треков всего у трёх артистов: ограничение обязано сработать.
        var snapshot = Library(50, artists: 3);

        var queue = Build(snapshot, Request(size: 6));

        var perArtist = queue
            .Select(item => snapshot.MetaAt(item.Row).ArtistId)
            .GroupBy(id => id)
            .Select(group => group.Count());

        Assert.All(perArtist, count => Assert.True(count <= 2, $"artist appears {count} times"));
    }

    [Fact]
    public void Byte_identical_copies_never_share_a_queue()
    {
        var snapshot = Library(30, contentHashes: row => $"hash-{row % 5}");

        var queue = Build(snapshot, Request(size: 8));
        var hashes = queue.Select(item => snapshot.MetaAt(item.Row).ContentHash).ToList();

        // Различных файлов всего пять, поэтому очередь честно короче запрошенных восьми:
        // недобор здесь правильнее повтора.
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
        Assert.True(queue.Count <= 5, $"queue held {queue.Count} items for 5 distinct files");
    }

    [Fact]
    public void The_same_song_under_another_file_is_still_the_same_song()
    {
        var snapshot = Library(30, songKeys: row => $"artist|title-{row % 4}");

        var queue = Build(snapshot, Request(size: 8));
        var songs = queue.Select(item => snapshot.MetaAt(item.Row).SongKey).ToList();

        Assert.Equal(songs.Count, songs.Distinct().Count());
        Assert.True(queue.Count <= 4, $"queue held {queue.Count} items for 4 distinct songs");
    }

    [Fact]
    public void Exploration_never_opens_the_queue()
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var queue = Build(Library(80), Request(size: 6, seed: seed));

            Assert.False(queue[0].Explore, $"seed {seed} opened the queue with an explore pick");
        }
    }

    [Fact]
    public void Exploration_really_is_far_from_the_taste()
    {
        var snapshot = Library(120);
        var queue = Build(snapshot, Request(size: 9, exploreRatio: 0.34));

        var explore = queue.Where(item => item.Explore).ToList();
        var exploit = queue.Where(item => !item.Explore).ToList();

        Assert.NotEmpty(explore);
        Assert.True(
            explore.Average(item => item.CosineTaste) < exploit.Average(item => item.CosineTaste),
            "explore picks should sit further from the taste than exploit picks");
    }

    [Fact]
    public void No_exploration_means_every_pick_is_close()
    {
        var queue = Build(Library(60), Request(size: 6, exploreRatio: 0));

        Assert.DoesNotContain(queue, item => item.Explore);
    }

    [Fact]
    public void Discovery_mode_fills_at_least_half_the_queue_with_exploration()
    {
        var queue = Build(Library(80), Request(size: 6, discover: true, exploreRatio: 0.1));

        Assert.True(queue.Count(item => item.Explore) >= 3);
    }

    [Fact]
    public void The_same_seed_builds_the_same_queue()
    {
        var snapshot = Library(70);

        var first = Build(snapshot, Request(size: 8, seed: 99));
        var second = Build(snapshot, Request(size: 8, seed: 99));

        Assert.Equal(first.Select(i => i.TrackId), second.Select(i => i.TrackId));
    }

    [Fact]
    public void A_transition_edge_lifts_the_track_it_points_at()
    {
        var snapshot = Library(40);
        var target = snapshot.MetaAt(6).TrackId;

        var without = Build(snapshot, Request(currentRow: 0, size: 30, exploreRatio: 0));
        var with = Build(snapshot, Request(
            currentRow: 0,
            size: 30,
            exploreRatio: 0,
            transitions: new Dictionary<Guid, double> { [target] = 50.0 }));

        var before = without.Single(item => item.TrackId == target);
        var after = with.Single(item => item.TrackId == target);

        // Ребро добавляет ровно свой терм — не больше. Оно подталкивает, а не решает:
        // 0.2 против разброса оценок примерно в 0.6, так что перевесить далёкий трек оно
        // не может и не должно.
        Assert.Equal(0.20, after.Score - before.Score, precision: 6);
        Assert.True(
            Rank(with, target) < Rank(without, target),
            "the edge should improve the track's position");
    }

    private static int Rank(IReadOnlyList<QueueItem> queue, Guid trackId)
    {
        for (var index = 0; index < queue.Count; index++)
        {
            if (queue[index].TrackId == trackId)
                return index;
        }

        return int.MaxValue;
    }

    [Fact]
    public void A_brand_new_track_is_surfaced_but_a_repeatedly_abandoned_one_is_not()
    {
        var fresh = Library(40, createdAt: row => row == 25 ? Now.AddDays(-1) : Now.AddYears(-2));
        var burned = Library(40,
            createdAt: row => row == 25 ? Now.AddDays(-1) : Now.AddYears(-2),
            skippedEarly: row => row == 25 ? 3 : 0);

        var freshQueue = Build(fresh, Request(size: 6, exploreRatio: 0));
        var burnedQueue = Build(burned, Request(size: 6, exploreRatio: 0));

        Assert.Contains(freshQueue, item => item.NewBoost);
        Assert.DoesNotContain(burnedQueue, item => item.Row == 25);
    }

    [Fact]
    public void New_tracks_never_take_over_the_queue()
    {
        // Вся библиотека свежая: квота обязана удержать их долю.
        var snapshot = Library(40, createdAt: _ => Now.AddDays(-1));

        var queue = Build(snapshot, Request(size: 6, exploreRatio: 0));

        Assert.True(queue.Count(item => item.NewBoost) <= 2, "new-boosted picks exceeded the cap");
    }

    [Fact]
    public void A_library_smaller_than_the_queue_is_returned_whole_rather_than_padded()
    {
        var snapshot = Library(4);

        var queue = Build(snapshot, Request(size: 10));

        Assert.True(queue.Count <= 4);
        Assert.Equal(queue.Count, queue.Select(item => item.TrackId).Distinct().Count());
    }

    [Fact]
    public void Without_a_current_track_the_queue_still_builds()
    {
        var queue = Build(Library(40), Request(currentRow: -1, size: 6));

        Assert.Equal(6, queue.Count);
    }

    /// <summary>
    /// Умолчания настроек — те же, что в продакшене: ширина far-корзины, её разброс и потолок
    /// на артиста читаются оттуда же, откуда их читают полки.
    /// </summary>
    private static IReadOnlyList<QueueItem> Build(EmbeddingSnapshot snapshot, QueueRequest request) =>
        QueueBuilder.Build(snapshot, request, new RecommendationOptions());

    private static QueueRequest Request(
        int currentRow = 0,
        int size = 6,
        double exploreRatio = 0.15,
        bool discover = false,
        IReadOnlySet<Guid>? exclude = null,
        IReadOnlyDictionary<Guid, double>? transitions = null,
        int seed = 7) =>
        new(
            currentRow,
            Taste: VectorMath.Normalized([1f, 0f, 0f, 0f]),
            Exclude: exclude ?? new HashSet<Guid>(),
            ExploreRatio: exploreRatio,
            Discover: discover,
            TransitionsFrom: transitions ?? new Dictionary<Guid, double>(),
            Size: size,
            Now: Now,
            Seed: seed);

    /// <summary>Библиотека из <paramref name="count"/> треков, разбросанных по вкусовой оси.</summary>
    private static EmbeddingSnapshot Library(
        int count,
        int artists = 0,
        Func<int, string>? contentHashes = null,
        Func<int, string>? songKeys = null,
        Func<int, DateTimeOffset>? createdAt = null,
        Func<int, int>? skippedEarly = null)
    {
        const int Dimension = 4;
        var random = new Random(4242);
        var matrix = new float[count * Dimension];
        var meta = new TrackVectorMeta[count];
        var artistIds = Enumerable.Range(0, Math.Max(1, artists)).Select(_ => Guid.NewGuid()).ToArray();

        for (var row = 0; row < count; row++)
        {
            var target = matrix.AsSpan(row * Dimension, Dimension);

            // Косинус к вкусу [1,0,0,0] убывает с номером строки, поэтому «далёкая корзина»
            // предсказуемо оказывается в хвосте.
            target[0] = 1f - row / (float)count;
            for (var i = 1; i < Dimension; i++)
                target[i] = (float)(random.NextDouble() * 0.5);

            VectorMath.NormalizeInPlace(target);

            meta[row] = new TrackVectorMeta(
                TrackId: Guid.NewGuid(),
                ArtistId: artists > 0 ? artistIds[row % artists] : Guid.NewGuid(),
                ContentHash: contentHashes?.Invoke(row) ?? $"hash-{row}",
                SongKey: songKeys?.Invoke(row) ?? $"artist-{row}|title-{row}",
                CreatedAt: createdAt?.Invoke(row) ?? Now.AddYears(-2),
                ClusterId: row % 3,
                ShownCount: 0,
                SkippedEarlyCount: skippedEarly?.Invoke(row) ?? 0);
        }

        return new EmbeddingSnapshot(matrix, meta, Dimension, Now);
    }
}
