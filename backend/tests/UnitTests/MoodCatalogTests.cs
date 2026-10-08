// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using App.Recommendations.Embeddings;
using App.Recommendations.Moods;
using Infrastructure.Audio;

namespace UnitTests.Recommendations;

public class MoodCatalogTests
{
    [Fact]
    public void The_shipped_vectors_load_for_the_audio_model()
    {
        var catalog = new MoodCatalog(NullLogger<MoodCatalog>.Instance);

        Assert.Equal(["workout", "party", "chill", "sleep", "happy", "sad"], catalog.All.Select(mood => mood.Key));
        Assert.All(catalog.All, mood =>
        {
            Assert.Equal(ClapAudioEmbedder.VectorDimension, mood.Vector.Length);
            Assert.Equal(1f, Vectors.Dot(mood.Vector, mood.Vector), 3);
        });
    }

    [Fact]
    public void Moods_are_found_by_key_regardless_of_case()
    {
        var catalog = Catalog();

        Assert.Equal("calm", catalog.Find("CALM")?.Key);
        Assert.Null(catalog.Find("unknown"));
    }

    [Fact]
    public void The_track_closest_to_a_mood_ranks_first_in_it()
    {
        var catalog = Catalog();
        var snapshot = Library(
            Vectors.Unit(1f, 0.1f, 0.3f),
            Vectors.Unit(0.1f, 1f, 0.3f),
            Vectors.Unit(0.5f, 0.5f, 0.3f));

        var loud = catalog.RanksIn(snapshot, catalog.Find("loud")!);
        var calm = catalog.RanksIn(snapshot, catalog.Find("calm")!);

        Assert.Equal(1f, loud[0]);
        Assert.Equal(1f, calm[1]);
        Assert.Equal(0f, loud[1]);
    }

    // Настроения, которого в библиотеке нет, не находится и среди «лучших из имеющегося».
    [Fact]
    public void A_mood_missing_from_the_library_has_no_fitting_tracks()
    {
        var raw = Enumerable.Range(0, 40).Select(row => -0.3f + row * 0.005f).ToArray();

        var scores = MoodCatalog.Score(raw);

        Assert.Equal(0, scores.MemberCount);
        Assert.Equal(1f, scores.Ranks[^1]);
    }

    [Fact]
    public void A_track_fits_when_it_leans_to_the_mood_and_stands_out_in_the_library()
    {
        // Половина библиотеки ближе к «непохожему», треть — к «похожему», и лишь часть из них заметно.
        float[] raw = [-0.2f, -0.2f, -0.2f, -0.1f, -0.1f, -0.1f, 0.01f, 0.05f, 0.3f, 0.35f, 0.4f, 0.01f];

        var scores = MoodCatalog.Score(raw);

        Assert.Equal([8, 9, 10], Enumerable.Range(0, raw.Length).Where(row => scores.Members[row]));
        Assert.Equal(3, scores.MemberCount);
    }

    [Fact]
    public void A_mood_with_too_few_fitting_tracks_is_not_offered()
    {
        var catalog = Catalog();
        var few = Library([.. Enumerable.Range(0, 30).Select(row =>
            row < MoodCatalog.MinimumMembers - 1 ? Vectors.Unit(1f, 0.1f, 0f) : Vectors.Unit(-1f, 0.1f, 0f))]);
        var many = Library([.. Enumerable.Range(0, 30).Select(row =>
            row < MoodCatalog.MinimumMembers + 2 ? Vectors.Unit(1f, 0.1f, 0f) : Vectors.Unit(-1f, 0.1f, 0f))]);

        Assert.DoesNotContain(catalog.AvailableIn(few), mood => mood.Key == "loud");
        Assert.Contains(catalog.AvailableIn(many), mood => mood.Key == "loud");
    }

    [Fact]
    public void Ranks_are_cached_per_snapshot()
    {
        var catalog = Catalog();
        var snapshot = Library(Vectors.Unit(1f, 0f, 0f), Vectors.Unit(0f, 1f, 0f));
        var mood = catalog.Find("loud")!;

        Assert.Same(catalog.RanksIn(snapshot, mood), catalog.RanksIn(snapshot, mood));
    }

    [Fact]
    public void A_vector_of_another_dimension_ranks_nothing()
    {
        var catalog = new MoodCatalog([new Mood("odd", Vectors.Unit(1f, 0f))]);
        var snapshot = Library(Vectors.Unit(1f, 0f, 0f));

        Assert.All(catalog.RanksIn(snapshot, catalog.All[0]), rank => Assert.Equal(0f, rank));
        Assert.False(catalog.ScoresIn(snapshot, catalog.All[0]).Playable);
    }

    private static MoodCatalog Catalog() => new([
        new Mood("loud", Vectors.Unit(1f, 0f, 0f)),
        new Mood("calm", Vectors.Unit(0f, 1f, 0f)),
    ]);

    private static EmbeddingSnapshot Library(params float[][] vectors)
    {
        var dimension = vectors[0].Length;
        var meta = vectors.Select((_, row) => new TrackVectorMeta(
            Guid.NewGuid(), Guid.NewGuid(), $"hash-{row}", $"song-{row}", DateTimeOffset.UnixEpoch, 0)).ToArray();

        return new EmbeddingSnapshot([.. vectors.SelectMany(vector => vector)], meta, dimension);
    }
}
