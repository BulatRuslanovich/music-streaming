// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using App.Dtos;
using App.Recommendations.Embeddings;
using Domain.Entities;
using Domain.Entities.Recommendations;
using Infrastructure.Persistence;
using Xunit;

namespace IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class LibraryToolsTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Two_uploads_of_one_recording_are_reported_together()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedWithTwinsAsync();

        var report = await ReportAsync(client);

        var group = Assert.Single(report.Groups);
        Assert.Equal(
            new[] { library.Track(0), library.Track(1) }.Order(),
            group.Tracks.Select(track => track.Id).Order());
        Assert.Equal(library.TrackIds.Count, report.ComparedTracks);
    }

    [Fact]
    public async Task Keeping_one_copy_moves_likes_playlists_and_history_onto_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedWithTwinsAsync();
        var (keep, drop) = (library.Track(0), library.Track(1));

        Guid playlistId;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = fixture.Clock.GetUtcNow();

            var playlist = new Playlist { UserId = library.UserId, Name = "Road", CreatedAt = now, UpdatedAt = now };
            db.Playlists.Add(playlist);
            db.PlaylistTracks.Add(new PlaylistTrack { PlaylistId = playlist.Id, TrackId = drop, Position = 0 });
            db.Favorites.Add(new Favorite { UserId = library.UserId, TrackId = drop, CreatedAt = now });
            db.ListeningHistory.Add(new ListeningHistoryEntry { UserId = library.UserId, TrackId = drop, PlayedAt = now, PlaybackPosition = 60 });
            await db.SaveChangesAsync(Cancel.Token);

            playlistId = playlist.Id;
        }

        var response = await client.PostAsJsonAsync(
            "/api/library/duplicates/merge", new MergeDuplicatesRequest(keep, [drop]), Cancel.Token);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Assert.False(await db.Tracks.AnyAsync(track => track.Id == drop, Cancel.Token));
            Assert.True(await db.Favorites.AnyAsync(f => f.UserId == library.UserId && f.TrackId == keep, Cancel.Token));
            Assert.True(await db.PlaylistTracks.AnyAsync(pt => pt.PlaylistId == playlistId && pt.TrackId == keep, Cancel.Token));
            Assert.True(await db.ListeningHistory.AnyAsync(h => h.TrackId == keep, Cancel.Token));
        }
    }

    [Fact]
    public async Task A_pair_marked_as_different_is_not_reported_again()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedWithTwinsAsync();

        var response = await client.PostAsJsonAsync(
            "/api/library/duplicates/dismiss",
            new DismissDuplicatesRequest([library.Track(1), library.Track(0)]),
            Cancel.Token);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Empty((await ReportAsync(client)).Groups);
    }

    [Fact]
    public async Task Listeners_cannot_see_or_merge_duplicates()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        await SeedWithTwinsAsync();
        var listener = await fixture.CreateSignedInClientAsync("duplicates-listener", "listener-password-1");

        var response = await listener.GetAsync("/api/library/duplicates", Cancel.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_map_places_every_analysed_track_inside_the_unit_square()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 3, tracksPerArtist: 4);
        await fixture.EmbedLibraryAsync();

        var map = await client.GetFromJsonAsync<LibraryMapDto>("/api/library/map", RecommendationApiFixture.Json, Cancel.Token);

        Assert.NotNull(map);
        Assert.Equal(library.TrackIds.Count, map.TotalTracks);
        Assert.Equal(library.TrackIds.Order(), map.Points.Select(point => point.Id).Order());
        Assert.All(map.Points, point =>
        {
            Assert.InRange(point.X, 0f, 1f);
            Assert.InRange(point.Y, 0f, 1f);
        });
    }

    [Fact]
    public async Task Looked_up_tracks_come_back_in_the_asked_order_with_their_cues()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 2, tracksPerArtist: 3);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TrackCues.Add(new TrackCues
            {
                TrackId = library.Track(2),
                SourceHash = "hash",
                Version = 1,
                Succeeded = true,
                AudioStartSeconds = 0.4,
                AudioEndSeconds = 180.5,
                OutroStartSeconds = 172,
                TempoBpm = 124,
                AnalyzedAt = fixture.Clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(Cancel.Token);
        }

        var response = await client.PostAsJsonAsync(
            "/api/tracks/lookup",
            new TrackLookupRequest([library.Track(2), Guid.NewGuid(), library.Track(0)]),
            Cancel.Token);
        var tracks = await response.Content.ReadFromJsonAsync<List<TrackDto>>(RecommendationApiFixture.Json, Cancel.Token);

        Assert.NotNull(tracks);
        Assert.Equal([library.Track(2), library.Track(0)], tracks.Select(track => track.Id));
        Assert.Equal(new TrackCuesDto(0.4, 180.5, 172, 124), tracks[0].Cues);
        Assert.Null(tracks[1].Cues);
    }

    [Fact]
    public async Task The_waveform_is_served_once_the_track_is_analysed()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 1, tracksPerArtist: 2);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TrackCues.Add(new TrackCues
            {
                TrackId = library.Track(0),
                SourceHash = "hash",
                Version = 2,
                Succeeded = true,
                AudioStartSeconds = 0,
                AudioEndSeconds = 180,
                OutroStartSeconds = 180,
                Waveform = [0, 128, 255],
                AnalyzedAt = fixture.Clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(Cancel.Token);
        }

        var waveform = await client.GetFromJsonAsync<WaveformDto>(
            $"/api/tracks/{library.Track(0)}/waveform", RecommendationApiFixture.Json, Cancel.Token);
        var missing = await client.GetAsync($"/api/tracks/{library.Track(1)}/waveform", Cancel.Token);

        Assert.Equal([0, 128, 255], waveform?.Peaks);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    // Треки 0 и 1 звучат одинаково (один вектор), длительности у них 180 и 181 с.
    private async Task<(SeededLibrary Library, HttpClient Client)> SeedWithTwinsAsync()
    {
        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 2, tracksPerArtist: 3);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.DuplicateDismissals.ExecuteDeleteAsync(Cancel.Token);

            var twin = Enumerable.Range(0, 32).Select(i => i == 3 ? 1f : 0f).ToArray();
            VectorMath.NormalizeInPlace(twin);

            foreach (var trackId in new[] { library.Track(0), library.Track(1) })
            {
                db.TrackEmbeddings.Add(new TrackEmbedding
                {
                    TrackId = trackId,
                    Vector = twin,
                    Dimension = twin.Length,
                    ModelId = "test",
                    Strategy = "test",
                    Succeeded = true,
                    AnalyzedAt = fixture.Clock.GetUtcNow(),
                });
            }

            await db.SaveChangesAsync(Cancel.Token);
        }

        await fixture.EmbedLibraryAsync();
        return (library, client);
    }

    private static async Task<DuplicateReportDto> ReportAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<DuplicateReportDto>(
            "/api/library/duplicates", RecommendationApiFixture.Json, Cancel.Token))!;
}
