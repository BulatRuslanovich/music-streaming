// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Recommendations.Scoring;
using Xunit;

using static MusicStreaming.UnitTests.Recommendations.CandidateBuilder;

namespace MusicStreaming.UnitTests.Recommendations;

public class DiversifierTests
{
    [Fact]
    public void One_artist_cannot_take_over_a_shelf()
    {
        var artist = Guid.CreateVersion7();
        var options = Options();

        var pool = SameArtist(20, artist);
        pool.AddRange(Enumerable.Range(0, 20).Select(_ => Candidate(score: 0.4)));

        var shelf = Diversifier.Select(pool, 12, options);

        var byThatArtist = shelf.Count(c => c.ArtistId == artist);

        Assert.Equal(12, shelf.Count);
        Assert.True(byThatArtist <= options.MaxPerArtist, $"{byThatArtist} tracks by one artist");
    }

    [Fact]
    public void An_album_cannot_take_over_a_shelf()
    {
        var album = Guid.CreateVersion7();
        var options = Options();

        var pool = Enumerable.Range(0, 10)
            .Select(index => Candidate(score: 1.0 - index * 0.01, albumId: album))
            .Concat(Enumerable.Range(0, 20).Select(_ => Candidate(score: 0.3)))
            .ToList();

        var shelf = Diversifier.Select(pool, 12, options);

        Assert.True(shelf.Count(c => c.AlbumId == album) <= options.MaxPerAlbum);
    }

    [Fact]
    public void A_genre_cannot_take_over_a_shelf()
    {
        var genre = Guid.CreateVersion7();
        var options = Options();

        var pool = Enumerable.Range(0, 20)
            .Select(index => Candidate(score: 1.0 - index * 0.01, genreId: genre))
            .Concat(Enumerable.Range(0, 20).Select(_ => Candidate(score: 0.3, genreId: Guid.CreateVersion7())))
            .ToList();

        var shelf = Diversifier.Select(pool, 12, options);

        Assert.True(shelf.Count(c => c.GenreId == genre) <= options.MaxPerGenre);
    }

    [Fact]
    public void The_best_candidate_is_always_selected()
    {
        var best = Candidate(score: 0.99);
        var pool = Enumerable.Range(0, 30).Select(_ => Candidate(score: 0.5)).Append(best).ToList();

        var shelf = Diversifier.Select(pool, 12, Options());

        Assert.Contains(best, shelf);
        Assert.Equal(best, shelf[0]);
    }

    [Fact]
    public void Caps_give_way_rather_than_return_a_stub_shelf()
    {
        var pool = SameArtist(20, Guid.CreateVersion7());

        var shelf = Diversifier.Select(pool, 12, Options());

        Assert.Equal(12, shelf.Count);
    }

    [Fact]
    public void A_pool_smaller_than_the_shelf_is_returned_whole()
    {
        var pool = Enumerable.Range(0, 3).Select(_ => Candidate()).ToList();

        Assert.Equal(3, Diversifier.Select(pool, 12, Options()).Count);
    }

    [Fact]
    public void An_empty_pool_yields_an_empty_shelf() =>
        Assert.Empty(Diversifier.Select([], 12, Options()));

    [Fact]
    public void A_zero_length_shelf_selects_nothing() =>
        Assert.Empty(Diversifier.Select([Candidate()], 0, Options()));

    [Fact]
    public void Nothing_is_selected_twice()
    {
        var pool = Enumerable.Range(0, 40).Select(_ => Candidate(score: Random.Shared.NextDouble())).ToList();

        var shelf = Diversifier.Select(pool, 12, Options());

        Assert.Equal(shelf.Count, shelf.Select(c => c.TrackId).Distinct().Count());
    }

    [Fact]
    public void Previously_selected_candidates_count_against_the_caps()
    {
        var artist = Guid.CreateVersion7();
        var options = Options();

        var alreadySelected = SameArtist(options.MaxPerArtist, artist);
        var pool = SameArtist(10, artist);
        pool.AddRange(Enumerable.Range(0, 10).Select(_ => Candidate(score: 0.2)));

        var shelf = Diversifier.Select(pool, 6, options, alreadySelected);

        Assert.DoesNotContain(shelf, c => c.ArtistId == artist);
    }

    [Fact]
    public void The_same_track_is_maximally_similar_to_itself()
    {
        var candidate = Candidate();

        Assert.Equal(1.0, Diversifier.MetadataSimilarity(candidate, candidate));
    }

    [Fact]
    public void Similarity_ranks_album_above_artist_above_genre()
    {
        var artist = Guid.CreateVersion7();
        var album = Guid.CreateVersion7();
        var genre = Guid.CreateVersion7();

        var reference = Candidate(artistId: artist, albumId: album, genreId: genre);

        var sameAlbum = Diversifier.MetadataSimilarity(
            reference, Candidate(artistId: artist, albumId: album, genreId: genre));
        var sameArtist = Diversifier.MetadataSimilarity(
            reference, Candidate(artistId: artist, genreId: genre));
        var sameGenre = Diversifier.MetadataSimilarity(
            reference, Candidate(genreId: genre));

        Assert.True(sameAlbum > sameArtist);
        Assert.True(sameArtist > sameGenre);
        Assert.True(sameGenre > 0);
    }

    [Fact]
    public void Unrelated_candidates_of_unknown_vintage_are_not_similar()
    {
        var left = Candidate();
        var right = Candidate();

        Assert.Equal(0, Diversifier.MetadataSimilarity(left, right));
    }

    [Fact]
    public void A_shared_credit_counts_as_the_same_artist()
    {
        var shared = Guid.CreateVersion7();

        var primary = Candidate(artistId: shared);
        var collaboration = new RecommendationCandidate
        {
            TrackId = Guid.CreateVersion7(),
            ArtistId = Guid.CreateVersion7(),
            ArtistIds = [Guid.CreateVersion7(), shared],
        };

        Assert.Equal(0.8, Diversifier.MetadataSimilarity(collaboration, primary));
    }

    [Fact]
    public void Tracks_that_sound_alike_are_not_counted_as_variety()
    {
        var left = Candidate(genreId: Guid.CreateVersion7());
        var right = Candidate(genreId: Guid.CreateVersion7());

        // Без эмбеддингов о звучании ничего не известно, и разные жанры — это разнообразие.
        Assert.Equal(0, Diversifier.Similarity(left, right));

        left.EmbeddingRow = 0;
        right.EmbeddingRow = 1;

        Assert.True(Diversifier.Similarity(left, right, Vectors(0.93)) > 0.5);
    }

    [Fact]
    public void A_contrasting_arrangement_still_reads_as_variety()
    {
        var calm = Candidate(genreId: Guid.CreateVersion7());
        calm.EmbeddingRow = 0;

        var driving = Candidate(genreId: Guid.CreateVersion7());
        driving.EmbeddingRow = 1;

        Assert.True(Diversifier.Similarity(calm, driving, Vectors(0.2)) < 0.2);
    }

    [Fact]
    public void A_cosine_below_the_floor_carries_no_information()
    {
        var left = Candidate(genreId: Guid.CreateVersion7());
        var right = Candidate(genreId: Guid.CreateVersion7());
        left.EmbeddingRow = 0;
        right.EmbeddingRow = 1;

        Assert.Equal(0, Diversifier.SonicSimilarity(left, right, Vectors(0.35)));
    }

    [Fact]
    public void Sounding_alike_never_outweighs_sharing_an_artist()
    {
        var artist = Guid.CreateVersion7();
        var left = Candidate(artistId: artist);
        var right = Candidate(artistId: artist);

        left.EmbeddingRow = 0;
        right.EmbeddingRow = 1;

        // Потолок звучания (0.85) ниже ступени «тот же альбом» (0.9), но выше «тот же артист»
        // быть не должен только там, где метаданные говорят больше. Здесь совпадают оба,
        // и берётся максимум.
        Assert.Equal(0.85, Diversifier.Similarity(left, right, Vectors(1.0)), precision: 10);
    }

    [Fact]
    public void An_identical_sound_never_reaches_the_same_album_step()
    {
        var left = Candidate(genreId: Guid.CreateVersion7());
        var right = Candidate(genreId: Guid.CreateVersion7());
        left.EmbeddingRow = 0;
        right.EmbeddingRow = 1;

        Assert.True(Diversifier.SonicSimilarity(left, right, Vectors(1.0)) < 0.9);
    }

    [Fact]
    public void A_candidate_without_an_embedding_contributes_no_sonic_similarity()
    {
        var embedded = Candidate(genreId: Guid.CreateVersion7());
        embedded.EmbeddingRow = 0;

        var unembedded = Candidate(genreId: Guid.CreateVersion7());

        Assert.Equal(0, Diversifier.SonicSimilarity(embedded, unembedded, Vectors(1.0)));
    }

    /// <summary>Заглушка, отвечающая одним и тем же косинусом на любую пару строк.</summary>
    private static IVectorSimilarity Vectors(double cosine) => new ConstantSimilarity(cosine);

    private sealed class ConstantSimilarity(double cosine) : IVectorSimilarity
    {
        public double Between(int rowA, int rowB) => cosine;
    }
}
