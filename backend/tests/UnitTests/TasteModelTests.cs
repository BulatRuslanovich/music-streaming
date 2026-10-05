// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using App.Recommendations.Embeddings;

namespace UnitTests.Recommendations;

public class TasteModelTests
{
    private const int Dimension = 8;

    [Fact]
    public void Nothing_embedded_means_no_taste()
    {
        var snapshot = Snapshot(Around(0, 3, seed: 1));

        Assert.True(TasteModel.Fit(snapshot, [new TastePull(Guid.NewGuid(), 1)]).IsEmpty);
        Assert.True(TasteModel.Fit(EmbeddingSnapshot.Empty, [new TastePull(Guid.NewGuid(), 1)]).IsEmpty);
    }

    [Fact]
    public void One_coherent_taste_stays_one_centre()
    {
        var snapshot = Snapshot(Around(0, 12, seed: 2));

        var taste = TasteModel.Fit(snapshot, Pulls(snapshot, 0..12, 1.0));

        var mode = Assert.Single(taste.Modes);
        Assert.Equal(1, mode.Share, precision: 6);
    }

    [Fact]
    public void Two_distant_tastes_get_a_centre_each_in_proportion_to_their_weight()
    {
        var snapshot = Snapshot([.. Around(0, 10, seed: 3), .. Around(1, 10, seed: 4)]);

        var taste = TasteModel.Fit(snapshot, [.. Pulls(snapshot, 0..10, 0.7), .. Pulls(snapshot, 10..20, 0.3)]);

        Assert.Equal(2, taste.Modes.Count);
        Assert.Equal(0.7, taste.Modes[0].Share, precision: 2);
        Assert.Equal(0.3, taste.Modes[1].Share, precision: 2);
        Assert.True(taste.Modes[0].Centre[0] > 0.9f);
        Assert.True(taste.Modes[1].Centre[1] > 0.9f);
    }

    [Fact]
    public void A_sliver_of_listening_does_not_get_its_own_centre()
    {
        var snapshot = Snapshot([.. Around(0, 18, seed: 5), .. Around(1, 2, seed: 6)]);

        var taste = TasteModel.Fit(snapshot, Pulls(snapshot, 0..20, 1.0));

        Assert.Single(taste.Modes);
    }

    [Fact]
    public void Both_tastes_are_close_while_the_sound_between_them_is_not()
    {
        var between = Vectors.Unit(1, 1, 0, 0, 0, 0, 0, 0);
        var snapshot = Snapshot([.. Around(0, 10, seed: 7), .. Around(1, 10, seed: 8), between]);

        var taste = TasteModel.Fit(snapshot, [.. Pulls(snapshot, 0..10, 0.65), .. Pulls(snapshot, 10..20, 0.35)]);
        var similarities = taste.SimilaritiesIn(snapshot);

        var weaker = similarities[10..20].Average();
        Assert.True(weaker > similarities[20] + 0.15, $"minor taste {weaker:F2}, in between {similarities[20]:F2}");
        Assert.True(similarities[0..10].Average() > similarities[20] + 0.15);
    }

    [Fact]
    public void Dislikes_bend_a_centre_but_never_turn_it_around()
    {
        var snapshot = Snapshot([.. Around(0, 4, seed: 9), .. Around(0, 4, seed: 10)]);

        var taste = TasteModel.Fit(snapshot, [.. Pulls(snapshot, 0..4, 0.1), .. Pulls(snapshot, 4..8, -1.0)]);

        var mode = Assert.Single(taste.Modes);
        Assert.True(mode.Centre[0] > 0, "Repulsion flipped the centre away from what the listener likes");
    }

    [Fact]
    public void Each_vector_belongs_to_the_nearest_centre()
    {
        var snapshot = Snapshot([.. Around(0, 10, seed: 11), .. Around(1, 10, seed: 12)]);
        var taste = TasteModel.Fit(snapshot, [.. Pulls(snapshot, 0..10, 0.6), .. Pulls(snapshot, 10..20, 0.4)]);

        Assert.Equal(0, taste.NearestMode(snapshot.Vector(3)));
        Assert.Equal(1, taste.NearestMode(snapshot.Vector(13)));
    }

    private static IEnumerable<TastePull> Pulls(EmbeddingSnapshot snapshot, Range rows, double weight)
    {
        var (offset, length) = rows.GetOffsetAndLength(snapshot.Count);

        return Enumerable.Range(offset, length).Select(row => new TastePull(snapshot.MetaAt(row).TrackId, weight));
    }

    private static List<float[]> Around(int axis, int count, int seed)
    {
        var random = new Random(seed);

        return
        [
            .. Enumerable.Range(0, count).Select(_ =>
            {
                var vector = Enumerable.Range(0, Dimension).Select(_ => (float)(random.NextDouble() * 0.3 - 0.15)).ToArray();
                vector[axis] += 1f;

                return Vectors.Unit(vector);
            }),
        ];
    }

    private static EmbeddingSnapshot Snapshot(List<float[]> rows)
    {
        var matrix = rows.SelectMany(row => row).ToArray();
        var meta = rows
            .Select((_, row) => new TrackVectorMeta(Guid.NewGuid(), Guid.NewGuid(), $"hash-{row}", $"song-{row}", DateTimeOffset.UnixEpoch, 0))
            .ToArray();

        return new EmbeddingSnapshot(matrix, meta, Dimension);
    }
}
