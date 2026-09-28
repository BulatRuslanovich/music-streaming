// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Domain.Entities;
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

        var similar = await fixture.NeighboursAsync(library.Track(0), 10);

        Assert.NotEmpty(similar);
        Assert.DoesNotContain(library.Track(0), similar);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.True(
            await db.Tracks.AnyAsync(
                t => similar.Contains(t.Id) && t.ArtistId == library.Artist(0), Cancel.Token),
            "none of the neighbours shares the seed's artist");
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
            .Join(db.Tracks, s => s.SimilarTrackId, t => t.Id,
                (s, t) => new { s.SimilarTrackId, s.Score, t.ArtistId })
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
    public async Task An_incremental_pass_lands_exactly_where_a_full_rebuild_would()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var changed = new[] { library.Track(2), library.Track(7), library.Track(13) };
            await db.Tracks
                .Where(track => changed.Contains(track.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(track => track.Year, 1977), Cancel.Token);
        }

        var pass = await fixture.RefreshSimilarityAsync();
        var incremental = await SnapshotAsync();

        Assert.True(pass.Ran, "the pass found nothing to do, so there is nothing to compare");
        Assert.False(pass.WholeLibrary, "the pass fell back to a full rebuild, so nothing incremental was tested");

        // Отпечатки сносятся, поэтому следующий проход обязан пересобрать всё целиком.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.TrackSimilarityStates.ExecuteDeleteAsync(Cancel.Token);
        }

        var rebuild = await fixture.RefreshSimilarityAsync();
        var full = await SnapshotAsync();

        Assert.True(rebuild.WholeLibrary);

        Assert.NotEmpty(full);
        Assert.Equal(full, incremental);
    }

    [Fact]
    public async Task A_pass_over_an_unchanged_library_rewrites_nothing()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        var before = await ComputedAtAsync();
        Assert.NotNull(before);

        var second = await fixture.RefreshSimilarityAsync();

        Assert.False(second.Ran, "an unchanged library still triggered a rebuild");
        Assert.Equal(before, await ComputedAtAsync());
        Assert.NotEmpty(await SnapshotAsync());
    }

    [Fact]
    public async Task A_new_track_reaches_the_neighbours_of_the_tracks_it_resembles()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        var neighbour = library.Track(0);
        Guid added;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var model = await db.Tracks.AsNoTracking().FirstAsync(t => t.Id == neighbour, Cancel.Token);

            var track = new Track
            {
                Title = "Late Arrival",
                NormalizedTitle = "late arrival",
                ArtistId = model.ArtistId,
                AlbumId = model.AlbumId,
                GenreId = model.GenreId,
                Year = model.Year,
                DurationSeconds = model.DurationSeconds,
                FilePath = "music/late-arrival.mp3",
                OriginalFileName = "late-arrival.mp3",
                ContentHash = "late-arrival",
                FileSize = 4_000_000,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.Tracks.Add(track);
            await db.SaveChangesAsync(Cancel.Token);

            db.TrackArtists.Add(new TrackArtist
            {
                TrackId = track.Id,
                ArtistId = model.ArtistId,
                Position = 0,
            });

            await db.SaveChangesAsync(Cancel.Token);
            added = track.Id;
        }

        await fixture.RefreshSimilarityAsync();

        // Соседа никто не трогал, но его список обязан был обновиться: новый трек попал в область.
        var similar = await fixture.NeighboursAsync(neighbour, 20);

        Assert.Contains(added, similar);
    }

    private async Task<List<string>> SnapshotAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await db.TrackSimilarities.AsNoTracking()
            .OrderBy(row => row.TrackId).ThenBy(row => row.SimilarTrackId)
            .Select(row => new
            {
                row.TrackId,
                row.SimilarTrackId,
                row.Score,
                row.ContentScore,
                row.CollabScore,
                row.Support,
            })
            .ToListAsync(Cancel.Token);

        return
        [
            .. rows.Select(row =>
                $"{row.TrackId}|{row.SimilarTrackId}|{row.Score:F9}|{row.ContentScore:F9}"
                + $"|{row.CollabScore:F9}|{row.Support}"),
        ];
    }

    private async Task<DateTimeOffset?> ComputedAtAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.TrackSimilarities.AsNoTracking()
            .MaxAsync(row => (DateTimeOffset?)row.ComputedAt, Cancel.Token);
    }
}
