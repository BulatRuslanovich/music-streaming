// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class SimilarTracksTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Tracks_by_the_same_artist_become_neighbours()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        var similar = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/recommendations/similar/{library.Track(0)}?limit=10", Cancel.Token);

        Assert.NotNull(similar);
        Assert.NotEmpty(similar);

        Assert.DoesNotContain(similar, item => item.Track.Id == library.Track(0));
        Assert.Contains(similar, item => item.Track.ArtistId == library.Artist(0));
        Assert.All(similar, item => Assert.Equal(ReasonKind, item.Reason.Kind));
    }

    [Fact]
    public async Task Shared_tags_make_neighbours_out_of_tracks_that_share_nothing_else()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        // Разные исполнители, разные альбомы, разные жанры и никакой общей истории: без тегов
        // эта пара вообще не попадает в кандидаты.
        var left = library.Track(2);
        var right = library.Track(7);

        await fixture.RefreshSimilarityAsync();

        var before = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/recommendations/similar/{left}?limit=20", Cancel.Token);

        Assert.DoesNotContain(before!, item => item.Track.Id == right);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            foreach (var trackId in new[] { left, right })
            {
                db.TrackTags.AddRange(
                    new TrackTag { TrackId = trackId, Name = "witch house", Weight = 1.0 },
                    new TrackTag { TrackId = trackId, Name = "darkwave", Weight = 0.9 },
                    new TrackTag { TrackId = trackId, Name = "coldwave", Weight = 0.8 });
            }

            await db.SaveChangesAsync(Cancel.Token);
        }

        await fixture.RefreshSimilarityAsync();

        var after = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/recommendations/similar/{left}?limit=20", Cancel.Token);

        Assert.Contains(after!, item => item.Track.Id == right);
    }

    [Fact]
    public async Task Timbre_decides_between_two_otherwise_equal_strangers()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        var seed = library.Track(2);

        // Обоим чужакам метаданные помогают одинаково слабо, но год и длительность чуть ближе у
        // Track(7) — если Track(17) всё равно выигрывает, дело именно в тембре.
        var alike = library.Track(17);
        var different = library.Track(7);

        var shape = Unit([1, 1, 1, 1, 1, -1, -1, -1, -1, -1]);
        var opposite = Unit([-1, -1, -1, -1, -1, 1, 1, 1, 1, 1]);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.TrackAudioFeatures.AddRange(
                Features(seed, shape),
                Features(alike, shape),
                Features(different, opposite));

            await db.SaveChangesAsync(Cancel.Token);
        }

        await fixture.RefreshSimilarityAsync();

        var similar = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/recommendations/similar/{seed}?limit=20", Cancel.Token);

        var ranked = similar!.Select(item => item.Track.Id).ToList();

        Assert.Contains(alike, ranked);
        Assert.True(
            !ranked.Contains(different) || ranked.IndexOf(alike) < ranked.IndexOf(different),
            "The track with the opposite timbre outranked the one that sounds the same");
    }

    [Fact]
    public async Task An_untagged_library_scores_exactly_as_it_did_before_tags_existed()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        var untagged = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/recommendations/similar/{library.Track(0)}?limit=10&debug=true", Cancel.Token);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Тег, который стоит ровно на одном треке, не может ни с чем пересечься.
            db.TrackTags.Add(new TrackTag { TrackId = library.Track(19), Name = "lone tag", Weight = 1.0 });
            await db.SaveChangesAsync(Cancel.Token);
        }

        await fixture.RefreshSimilarityAsync();

        var afterwards = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/recommendations/similar/{library.Track(0)}?limit=10&debug=true", Cancel.Token);

        Assert.Equal(
            untagged!.Select(item => item.Track.Id),
            afterwards!.Select(item => item.Track.Id));
    }

    [Fact]
    public async Task The_track_route_mirrors_the_recommendation_route()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        var viaTracks = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/tracks/{library.Track(0)}/similar?limit=5", Cancel.Token);

        var viaRecommendations = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/recommendations/similar/{library.Track(0)}?limit=5", Cancel.Token);

        Assert.Equal(
            viaRecommendations!.Select(item => item.Track.Id),
            viaTracks!.Select(item => item.Track.Id));
    }

    [Fact]
    public async Task A_track_with_no_computed_neighbours_still_answers()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.TrackSimilarities.CountAsync(Cancel.Token));
        }

        var similar = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/recommendations/similar/{library.Track(0)}?limit=10", Cancel.Token);

        Assert.NotNull(similar);
        Assert.NotEmpty(similar);
        Assert.DoesNotContain(similar, item => item.Track.Id == library.Track(0));
    }

    [Fact]
    public async Task An_unknown_track_is_a_not_found()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var response = await client.GetAsync($"/api/recommendations/similar/{Guid.CreateVersion7()}", Cancel.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_track_takes_its_neighbours_with_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var doomed = library.Track(0);
        Assert.True(await db.TrackSimilarities.AnyAsync(s => s.SimilarTrackId == doomed, Cancel.Token));

        await db.Tracks.Where(t => t.Id == doomed).ExecuteDeleteAsync(Cancel.Token);

        Assert.False(await db.TrackSimilarities.AnyAsync(s => s.TrackId == doomed, Cancel.Token));
        Assert.False(await db.TrackSimilarities.AnyAsync(s => s.SimilarTrackId == doomed, Cancel.Token));
    }

    [Fact]
    public async Task Similarity_is_stored_symmetrically()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pair = await db.TrackSimilarities.AsNoTracking().FirstAsync(Cancel.Token);

        var mirrored = await db.TrackSimilarities.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.TrackId == pair.SimilarTrackId && s.SimilarTrackId == pair.TrackId, Cancel.Token);

        Assert.NotNull(mirrored);
        Assert.Equal(pair.Score, mirrored.Score, precision: 10);
    }

    [Fact]
    public async Task Content_similarity_ranks_a_shared_artist_above_a_shared_genre()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var neighbours = await db.TrackSimilarities.AsNoTracking()
            .Where(s => s.TrackId == library.Track(0))
            .OrderByDescending(s => s.Score)
            .Select(s => new { s.SimilarTrackId, s.Score, ArtistId = s.SimilarTrack!.ArtistId })
            .ToListAsync(Cancel.Token);

        Assert.NotEmpty(neighbours);

        var bestSameArtist = neighbours.Where(n => n.ArtistId == library.Artist(0)).Max(n => n.Score);
        var bestOtherArtist = neighbours.Where(n => n.ArtistId != library.Artist(0))
            .Select(n => (double?)n.Score).Max() ?? 0;

        Assert.True(
            bestSameArtist > bestOtherArtist,
            $"Same artist scored {bestSameArtist}, a different one {bestOtherArtist}");
    }

    [Fact]
    public async Task Audio_features_connect_tracks_across_metadata_boundaries()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync();
        var first = library.Track(5);
        var second = library.Track(10);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TrackAudioFeatures.AddRange(
                Features(first, tempo: 120, energy: 0.72, brightness: 0.44),
                Features(second, tempo: 121, energy: 0.70, brightness: 0.45));
            await db.SaveChangesAsync(Cancel.Token);
        }

        await fixture.RefreshSimilarityAsync();

        using var assertionScope = fixture.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pair = await assertionDb.TrackSimilarities.AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.TrackId == first && item.SimilarTrackId == second,
                Cancel.Token);

        Assert.NotNull(pair);
        Assert.NotNull(pair.AudioScore);
        Assert.True(pair.AudioScore > 0.85, $"Audio similarity was only {pair.AudioScore}");
    }

    [Fact]
    public async Task A_track_still_waiting_for_re_analysis_is_not_punished_for_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync();
        var first = library.Track(5);
        var second = library.Track(10);

        // Смена версии алгоритма переанализирует библиотеку не мгновенно: пока бэкфилл идёт, у
        // одной стороны пары тембр уже есть, а у другой ещё нет. Счёт от этого меняться не должен.
        var withoutTimbre = await AudioScoreAsync(first, second, timbre: null);
        var halfAnalysed = await AudioScoreAsync(first, second, timbre: Unit([1, 1, 1, 1, 1, -1, -1, -1, -1, -1]));

        Assert.NotNull(withoutTimbre);
        Assert.NotNull(halfAnalysed);
        Assert.Equal(withoutTimbre.Value, halfAnalysed.Value, 6);
    }

    private async Task<double?> AudioScoreAsync(Guid first, Guid second, double[]? timbre)
    {
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.TrackAudioFeatures.ExecuteDeleteAsync(Cancel.Token);

            var left = Features(first, tempo: 120, energy: 0.72, brightness: 0.44);
            left.Timbre = timbre ?? [];

            db.TrackAudioFeatures.AddRange(
                left, Features(second, tempo: 121, energy: 0.70, brightness: 0.45));

            await db.SaveChangesAsync(Cancel.Token);
        }

        await fixture.RefreshSimilarityAsync();

        using var assertionScope = fixture.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await assertionDb.TrackSimilarities.AsNoTracking()
            .Where(item => item.TrackId == first && item.SimilarTrackId == second)
            .Select(item => item.AudioScore)
            .FirstOrDefaultAsync(Cancel.Token);
    }

    private static TrackAudioFeatures Features(
        Guid trackId, double tempo, double energy, double brightness) => new()
        {
            TrackId = trackId,
            TempoBpm = tempo,
            TempoConfidence = 0.9,
            Energy = energy,
            LoudnessDb = -10,
            Brightness = brightness,
            DynamicRangeDb = 8,
            AnalyzedSeconds = 180,
            AlgorithmVersion = 1,
            Succeeded = true,
            AnalyzedAt = DateTimeOffset.UtcNow,
        };

    private const string ReasonKind = "similarTo";

    private static TrackAudioFeatures Features(Guid trackId, double[] timbre) => new()
    {
        TrackId = trackId,
        TempoBpm = 120,
        TempoConfidence = 0.9,
        Energy = 0.5,
        LoudnessDb = -10,
        Brightness = 0.5,
        DynamicRangeDb = 10,
        SpectralRolloff = 0.5,
        Timbre = timbre,
        AnalyzedSeconds = 180,
        AlgorithmVersion = 2,
        Succeeded = true,
        AnalyzedAt = DateTimeOffset.UtcNow,
    };

    private static double[] Unit(double[] values)
    {
        var norm = Math.Sqrt(values.Sum(value => value * value));
        return [.. values.Select(value => value / norm)];
    }
}
