// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;

using static UnitTests.Recommendations.CandidateBuilder;
using App.Recommendations;
using App.Recommendations.Embeddings;
using App.Recommendations.Home;

namespace UnitTests.Recommendations;

public class DiversifierTests
{
    [Fact]
    public void One_artist_cannot_take_over_a_shelf()
    {
        var artist = Guid.CreateVersion7();

        var pool = SameArtist(20, artist);
        pool.AddRange(Enumerable.Range(0, 20).Select(_ => Candidate(score: 0.4)));

        var shelf = Diversifier.Select(pool, 12);

        var byThatArtist = shelf.Count(c => c.ArtistId == artist);

        Assert.Equal(12, shelf.Count);
        Assert.True(byThatArtist <= RecommendationTuning.Diversity.MaxPerArtist, $"{byThatArtist} tracks by one artist");
    }

    [Fact]
    public void An_album_cannot_take_over_a_shelf()
    {
        var album = Guid.CreateVersion7();

        var pool = Enumerable.Range(0, 10)
            .Select(index => Candidate(score: 1.0 - index * 0.01, albumId: album))
            .Concat(Enumerable.Range(0, 20).Select(_ => Candidate(score: 0.3)))
            .ToList();

        var shelf = Diversifier.Select(pool, 12);

        Assert.True(shelf.Count(c => c.AlbumId == album) <= RecommendationTuning.Diversity.MaxPerAlbum);
    }

    [Fact]
    public void A_genre_cannot_take_over_a_shelf()
    {
        var genre = Guid.CreateVersion7();

        var pool = Enumerable.Range(0, 20)
            .Select(index => Candidate(score: 1.0 - index * 0.01, genreId: genre))
            .Concat(Enumerable.Range(0, 20).Select(_ => Candidate(score: 0.3, genreId: Guid.CreateVersion7())))
            .ToList();

        var shelf = Diversifier.Select(pool, 12);

        Assert.True(shelf.Count(c => c.GenreId == genre) <= RecommendationTuning.Diversity.MaxPerGenre);
    }

    [Fact]
    public void The_best_candidate_is_always_selected()
    {
        var best = Candidate(score: 0.99);
        var pool = Enumerable.Range(0, 30).Select(_ => Candidate(score: 0.5)).Append(best).ToList();

        var shelf = Diversifier.Select(pool, 12);

        Assert.Contains(best, shelf);
        Assert.Equal(best, shelf[0]);
    }

    [Fact]
    public void Caps_give_way_rather_than_return_a_stub_shelf()
    {
        var pool = SameArtist(20, Guid.CreateVersion7());

        var shelf = Diversifier.Select(pool, 12);

        Assert.Equal(12, shelf.Count);
    }

    [Fact]
    public void A_pool_smaller_than_the_shelf_is_returned_whole()
    {
        var pool = Enumerable.Range(0, 3).Select(_ => Candidate()).ToList();

        Assert.Equal(3, Diversifier.Select(pool, 12).Count);
    }

    [Fact]
    public void An_empty_pool_yields_an_empty_shelf() =>
        Assert.Empty(Diversifier.Select([], 12));

    [Fact]
    public void A_zero_length_shelf_selects_nothing() =>
        Assert.Empty(Diversifier.Select([Candidate()], 0));

    [Fact]
    public void Nothing_is_selected_twice()
    {
        var pool = Enumerable.Range(0, 40).Select(_ => Candidate(score: Random.Shared.NextDouble())).ToList();

        var shelf = Diversifier.Select(pool, 12);

        Assert.Equal(shelf.Count, shelf.Select(c => c.TrackId).Distinct().Count());
    }

    [Fact]
    public void Previously_selected_candidates_count_against_the_caps()
    {
        var artist = Guid.CreateVersion7();

        var alreadySelected = SameArtist(RecommendationTuning.Diversity.MaxPerArtist, artist);
        var pool = SameArtist(10, artist);
        pool.AddRange(Enumerable.Range(0, 10).Select(_ => Candidate(score: 0.2)));

        var shelf = Diversifier.Select(pool, 6, alreadySelected);

        Assert.DoesNotContain(shelf, c => c.ArtistId == artist);
    }

    [Fact]
    public void Tracks_that_sound_alike_are_not_counted_as_variety()
    {
        // Строки: 0 — уже на полке, 1 — звучит почти так же, 2 — звучит иначе.
        TrackVectorMeta Meta() => new(Guid.NewGuid(), Guid.NewGuid(), "", "", default, 0);
        var vectors = new EmbeddingSnapshot([1f, 0f, 0.99f, 0.14f, 0f, 1f], [Meta(), Meta(), Meta()], 2);

        var shelved = Candidate(score: 1.0, genreId: Guid.CreateVersion7(), embeddingRow: 0);
        var alike = Candidate(score: 0.9, genreId: Guid.CreateVersion7(), embeddingRow: 1);
        var different = Candidate(score: 0.85, genreId: Guid.CreateVersion7(), embeddingRow: 2);

        var next = Diversifier.Select([alike, different], 1, [shelved], vectors: vectors);

        Assert.Equal(different.TrackId, Assert.Single(next).TrackId);
    }
}
