// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
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

        Assert.Equal(0, Diversifier.Similarity(left, right));

        left.AudioProfile = new TrackAudioProfile(TempoBpm: 128, Energy: 0.8, Brightness: 0.6);
        right.AudioProfile = new TrackAudioProfile(TempoBpm: 128, Energy: 0.8, Brightness: 0.6);

        Assert.True(Diversifier.Similarity(left, right) > 0.5);
    }

    [Fact]
    public void A_contrasting_arrangement_still_reads_as_variety()
    {
        var calm = Candidate(genreId: Guid.CreateVersion7());
        calm.AudioProfile = new TrackAudioProfile(TempoBpm: 70, Energy: 0.2, Brightness: 0.1);

        var driving = Candidate(genreId: Guid.CreateVersion7());
        driving.AudioProfile = new TrackAudioProfile(TempoBpm: 170, Energy: 0.9, Brightness: 0.8);

        Assert.True(Diversifier.Similarity(calm, driving) < 0.2);
    }

    [Fact]
    public void Sounding_alike_never_outweighs_sharing_an_artist()
    {
        var artist = Guid.CreateVersion7();
        var left = Candidate(artistId: artist);
        var right = Candidate(artistId: artist);

        left.AudioProfile = new TrackAudioProfile(TempoBpm: 128, Energy: 0.8, Brightness: 0.6);
        right.AudioProfile = new TrackAudioProfile(TempoBpm: 128, Energy: 0.8, Brightness: 0.6);

        Assert.Equal(0.8, Diversifier.Similarity(left, right));
    }
}
